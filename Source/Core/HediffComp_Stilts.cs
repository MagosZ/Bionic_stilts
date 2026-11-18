using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StiltsBionics
{
    public enum StiltsState : byte
    {
        Retracted,
        Extending,
        Extended,
        Retracting,
    }

    /// <summary>Tunables of the stilts, set on SB_StiltsControl in XML.</summary>
    public class HediffCompProperties_Stilts : HediffCompProperties
    {
        /// <summary>Ticks to extend or retract (2 s). The pawn stands still and can't attack meanwhile.</summary>
        public int transitionTicks = 120;

        /// <summary>How far the body is lifted, in Combat Extended height units (1.0 = a 1.75 m human): 2.43 makes the
        /// pawn 6 m tall. Also the lift on screen, in cells.</summary>
        public float lift = 2.43f;

        /// <summary>Total blunt damage of a fall, spread over fallHits random body parts, and the stun after it.</summary>
        public float fallDamage = 20f;
        public int fallHits = 4;
        public int fallStunTicks = 180;

        /// <summary>A stilt lost while the stilts are at least this far out makes the pawn fall.</summary>
        public float fallAboveFraction = 0.25f;

        /// <summary>Damage to the stilted legs is multiplied by this while extended (Damage.cs). The walking bonus is
        /// the extended stage's MoveSpeed factor in XML.</summary>
        public float legDamageFactor = 1.5f;

        public HediffCompProperties_Stilts()
        {
            compClass = typeof(HediffComp_Stilts);
        }
    }

    /// <summary>The extend state of a pawn's stilts. Ticked from the pawn's path follower (see Movement.cs), which
    /// runs every tick for every spawned pawn.</summary>
    public class HediffComp_Stilts : HediffComp
    {
        private StiltsState state;
        private int transitionStart;
        private bool autoRetracted;
        private bool pendingFall;
        private bool pendingStiltCheck;

        public HediffCompProperties_Stilts Props => (HediffCompProperties_Stilts)props;

        public StiltsState State => state;

        /// <summary>Fully up: the 6 m rules (height, range, cover, line of sight) apply only then.</summary>
        public bool Extended => state == StiltsState.Extended;

        public bool InTransition => state == StiltsState.Extending || state == StiltsState.Retracting;

        /// <summary>How far out the stilts are, 0..1 (drawing). Read-only, safe from drawing threads.</summary>
        public float Fraction
        {
            get
            {
                switch (state)
                {
                    case StiltsState.Extended:
                        return 1f;
                    case StiltsState.Extending:
                        return Progress;
                    case StiltsState.Retracting:
                        return 1f - Progress;
                    default:
                        return 0f;
                }
            }
        }

        private float Progress
        {
            get
            {
                int ticks = Mathf.Max(1, Props.transitionTicks);
                return Mathf.Clamp01((Find.TickManager.TicksGame - transitionStart) / (float)ticks);
            }
        }

        // ---- Lifecycle ----

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            Stilts.Register(Pawn, this);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Stilts.Unregister(Pawn, this);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref state, "state", StiltsState.Retracted);
            Scribe_Values.Look(ref transitionStart, "transitionStart", 0);
            Scribe_Values.Look(ref autoRetracted, "autoRetracted", false);
            Scribe_Values.Look(ref pendingFall, "pendingFall", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Pawn != null)
            {
                // Usually too early (the pawn is set afterwards); MovementPatch_HediffSetExposeData registers then.
                Stilts.Register(Pawn, this);
            }
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                switch (state)
                {
                    case StiltsState.Extended:
                        return "StiltsBionics_StateExtended".Translate();
                    case StiltsState.Extending:
                        return "StiltsBionics_StateExtending".Translate();
                    case StiltsState.Retracting:
                        return "StiltsBionics_StateRetracting".Translate();
                    default:
                        return "StiltsBionics_StateRetracted".Translate();
                }
            }
        }

        // ---- Commands ----

        public void StartExtend()
        {
            switch (state)
            {
                case StiltsState.Retracted:
                    state = StiltsState.Extending;
                    transitionStart = Find.TickManager.TicksGame;
                    break;
                case StiltsState.Retracting:
                    Reverse(StiltsState.Extending);
                    break;
            }
            autoRetracted = false;
        }

        /// <summary>auto: started by the pawn walking toward a roof. A drafted pawn then extends again by itself when
        /// it stops on open ground.</summary>
        public void StartRetract(bool auto)
        {
            switch (state)
            {
                case StiltsState.Extended:
                    state = StiltsState.Retracting;
                    transitionStart = Find.TickManager.TicksGame;
                    break;
                case StiltsState.Extending:
                    Reverse(StiltsState.Retracting);
                    break;
            }
            autoRetracted = auto && Pawn.Drafted;
        }

        /// <summary>Turns a running transition around from where it is.</summary>
        private void Reverse(StiltsState to)
        {
            int ticks = Mathf.Max(1, Props.transitionTicks);
            float done = Progress;
            state = to;
            transitionStart = Find.TickManager.TicksGame - Mathf.RoundToInt((1f - done) * ticks);
        }

        private void RetractAtOnce()
        {
            state = StiltsState.Retracted;
            autoRetracted = false;
        }

        public bool CanExtendNow(out string reason)
        {
            Pawn pawn = Pawn;
            reason = null;
            if (!pawn.Spawned)
            {
                reason = "StiltsBionics_NotSpawned".Translate();
            }
            else if (pawn.Downed || pawn.GetPosture() != PawnPosture.Standing)
            {
                reason = "StiltsBionics_NotStanding".Translate(pawn.LabelShort, pawn);
            }
            else if (!Stilts.HasAllStilts(pawn))
            {
                reason = "StiltsBionics_StiltMissing".Translate(pawn.LabelShort, pawn);
            }
            else if (pawn.Position.Roofed(pawn.Map))
            {
                reason = "StiltsBionics_UnderRoof".Translate();
            }
            return reason == null;
        }

        public Command GetToggleGizmo()
        {
            bool goingUp = state == StiltsState.Extended || state == StiltsState.Extending;
            var command = new Command_Action
            {
                defaultLabel = (goingUp ? "StiltsBionics_Retract" : "StiltsBionics_Extend").Translate(),
                defaultDesc = (goingUp ? "StiltsBionics_RetractDesc" : "StiltsBionics_ExtendDesc").Translate(),
                icon = goingUp ? StiltsTex.Retract : StiltsTex.Extend,
            };
            if (goingUp)
            {
                command.action = () => StartRetract(false);
            }
            else
            {
                command.action = StartExtend;
                if (!CanExtendNow(out string reason))
                {
                    command.Disable(reason);
                }
            }
            return command;
        }

        // ---- Ticking ----

        /// <summary>A stilt hediff was removed (shot or cut off, or replaced). Acted on at the next tick.</summary>
        public void Notify_StiltLost()
        {
            if (Fraction >= Props.fallAboveFraction)
            {
                pendingFall = true;
            }
            pendingStiltCheck = true;
        }

        /// <summary>Called every tick for the spawned pawn, before its path follower moves it. Returns false to keep
        /// the pawn standing still this tick (the stilts are moving, or it just reached a roof).</summary>
        public bool PatherTick(Pawn_PathFollower pather)
        {
            bool move = TickState(pather);
            SyncSeverity();
            return move;
        }

        /// <summary>The hediff's severity carries the extend state, so its extended stage (MoveSpeed x1.1 in XML)
        /// applies exactly while the stilts are fully up; the health system refreshes stats on a stage change.</summary>
        public void SyncSeverity()
        {
            float wanted = state == StiltsState.Extended ? 1f : 0.5f;
            if (parent != null && parent.Severity != wanted)
            {
                parent.Severity = wanted;
            }
        }

        /// <summary>The pawn left the map (caravan, transport pod, death): the stilts fold.</summary>
        public void Notify_DeSpawned()
        {
            RetractAtOnce();
            SyncSeverity();
        }

        private bool TickState(Pawn_PathFollower pather)
        {
            Pawn pawn = Pawn;
            Map map = pawn.Map;
            if (map == null)
            {
                return true;
            }
            if (pendingFall)
            {
                pendingFall = false;
                Fall();
            }
            if (pendingStiltCheck)
            {
                pendingStiltCheck = false;
                if (Stilts.StiltCount(pawn) == 0)
                {
                    // No stilt left: the controller goes too (outside any hediff loop, so it is safe here).
                    RetractAtOnce();
                    pawn.health.RemoveHediff(parent);
                    return true;
                }
                if (state != StiltsState.Retracted && !Stilts.HasAllStilts(pawn))
                {
                    RetractAtOnce();
                }
            }

            bool roofed = pawn.Position.Roofed(map);
            bool standing = !pawn.Downed && pawn.GetPosture() == PawnPosture.Standing;
            int ticks = Mathf.Max(1, Props.transitionTicks);
            switch (state)
            {
                case StiltsState.Extending:
                    if (!standing)
                    {
                        RetractAtOnce();
                    }
                    else if (roofed)
                    {
                        Reverse(StiltsState.Retracting);
                    }
                    else if (Find.TickManager.TicksGame - transitionStart >= ticks)
                    {
                        state = StiltsState.Extended;
                    }
                    break;
                case StiltsState.Retracting:
                    if (!standing || Find.TickManager.TicksGame - transitionStart >= ticks)
                    {
                        state = StiltsState.Retracted;
                    }
                    break;
                case StiltsState.Extended:
                    if (!standing)
                    {
                        // Downed, asleep or lying: the stilts fold under the pawn.
                        RetractAtOnce();
                    }
                    else if (roofed)
                    {
                        // A roof appeared overhead (built, or the pawn was moved under one).
                        StartRetract(false);
                        if (pawn.Faction == Faction.OfPlayer)
                        {
                            Messages.Message("StiltsBionics_RoofRetract".Translate(pawn.LabelShort, pawn), pawn,
                                MessageTypeDefOf.NeutralEvent, false);
                        }
                    }
                    break;
                case StiltsState.Retracted:
                    if (autoRetracted)
                    {
                        if (!pawn.Drafted)
                        {
                            autoRetracted = false;
                        }
                        else if (!pather.Moving && !roofed && CanExtendNow(out _))
                        {
                            StartExtend();
                        }
                    }
                    break;
            }

            if (InTransition)
            {
                return false;
            }
            if (state == StiltsState.Extended && pather.Moving)
            {
                IntVec3 next = pather.nextCell;
                if (next != pawn.Position && next.InBounds(map) && next.Roofed(map))
                {
                    // About to walk under a roof: stop at the edge and retract first.
                    StartRetract(true);
                    return false;
                }
            }
            return true;
        }

        /// <summary>A stilt gave way while up: the pawn drops from 6 m.</summary>
        private void Fall()
        {
            Pawn pawn = Pawn;
            RetractAtOnce();
            SyncSeverity();
            if (pawn.Dead || !pawn.Spawned)
            {
                return;
            }
            int hits = Mathf.Max(1, Props.fallHits);
            float perHit = Props.fallDamage / hits;
            for (int i = 0; i < hits && !pawn.Dead; i++)
            {
                BodyPartRecord part = pawn.health.hediffSet
                    .GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
                    .RandomElementWithFallback();
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, perHit, 0f, -1f, null, part));
            }
            if (pawn.Dead)
            {
                return;
            }
            pawn.stances?.stunner?.StunFor(Props.fallStunTicks, null, false);
            if (pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer)
            {
                Messages.Message("StiltsBionics_Fell".Translate(pawn.LabelShort, pawn), pawn,
                    MessageTypeDefOf.NegativeHealthEvent, true);
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class StiltsTex
    {
        public static readonly Texture2D Extend = ContentFinder<Texture2D>.Get("StiltsBionics/UI/Extend");
        public static readonly Texture2D Retract = ContentFinder<Texture2D>.Get("StiltsBionics/UI/Retract");
    }
}
