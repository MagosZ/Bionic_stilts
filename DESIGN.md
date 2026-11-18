# Stilts Bionics: design

Bionic legs with telescopic stilts. Extended, the pawn stands 6 m tall: more range, cover ignored, walls near it
cleared; but it can't walk under a roof. Agreed with the user 2026-11-18. RimWorld 1.6 and 1.7, Harmony required,
Combat Extended optional.

## 0. Build and layout

- `Source/StiltsBionics.csproj` -> `Assemblies/StiltsBionics.dll`: everything except the CE rules. net48 against
  Krafs 1.6.4871; `-p:RW=1.7` compiles it against the 1.7 reference assemblies as an API check (output not shipped).
  It names no CE type.
- `Source/CE/StiltsBionicsCE.csproj` -> `CE/Assemblies/StiltsBionicsCE.dll`: the CE rules. References the main DLL
  and CE's DLL (neither copied). `LoadFolders.xml` loads the `CE` folder only with `CETeam.CombatExtended`.
- Build both: `dotnet build Source/CE/StiltsBionicsCE.csproj -c Release -nodeReuse:false -p:UseSharedCompilation=false`.
- Both DLLs apply their `[HarmonyPatch]` classes one by one (`StiltsMod.PatchAllSafely`): a patch whose target is
  missing in a game version is skipped with a warning, the rest still apply. Private targets are resolved with
  `AccessTools` and `Prepare()` guards.
- Art: `py -3 Source/Art/make_textures.py` writes every texture.

## 1. Items, hediffs, surgery

- `SB_BionicStilts` (item, the pair): BodyPartBionicBase, fabrication bench, Bionics research, 40 plasteel + 10
  advanced components, work 52000 (two bionic legs: 30 + 8, 2 x 26000), mass 16. No `techHediffsTags`, so pawn
  generation never fits it (its surgery is one bill for two legs).
- `SB_BionicStilt` (one per leg, `Hediff_BionicStilt : Hediff_AddedPart`): partEfficiency 1.25 (bionic leg). No
  `spawnThingOnRemoved`. Its PostAdd adds the controller if missing; its PostRemoved tells the controller.
- `SB_StiltsControl` (whole body, `HediffComp_Stilts`): the extend state, shown in the label ("extended" etc.).
  Removed by the controller itself when no stilt is left.
- `SB_InstallBionicStilts` (`Recipe_InstallBionicStilts`, targetsBodyPart false): available on humanlikes with at
  least two Leg parts (parent present) that don't all have a stilt. One surgery-fail roll, then per leg: vanilla
  `RestorePartAndSpawnAllPreviousParts` (old artificial legs drop as items) and a stilt. Ideology prosthetic event.
- `SB_RemoveBionicStilts` (`Recipe_RemoveBionicStilts`): cuts every stilted leg off (SurgicalCut 99999, as vanilla's
  part removal), legs end missing; the pair item drops only if every leg still had its stilt.
- Vanilla's per-part surgeries (`Recipe_RemoveBodyPart`, install artificial/natural part, install implant) skip
  legs with a stilt (postfix on their `GetPartsToApplyOn`), so the pair can't be split by surgery.

## 2. Extend and retract (`HediffComp_Stilts`, `Movement.cs`)

- States Retracted, Extending, Extended, Retracting; transitions take `transitionTicks` (120 = 2 s). A transition
  can be reversed mid-way from where it is. Only fully Extended counts for the 6 m rules.
- Gizmo on player-controlled colonists (postfix on `Pawn.GetGizmos`): "Extend stilts" (disabled: not spawned, not
  standing, a stilt missing, under a roof) / "Retract stilts".
- The clock is the path follower: prefix on `Pawn_PathFollower.PatherTick` (every tick, every spawned pawn) calls
  `HediffComp_Stilts.PatherTick`; returning false keeps the pawn still that tick. During a transition the pawn does
  not move, and `Verb.TryStartCastOn` (all overloads) refuses attacks.
- Roofs (user): an extended pawn whose next path cell is roofed stops at the edge and retracts first
  (auto-retract), then walks on. No path-finding change (1.6's path finder is Burst-compiled). A drafted pawn whose
  retract was automatic extends again when it stops moving on unroofed ground; an undrafted one stays retracted.
- Doors never block (user). A roofed doorway is a roof.
- A roof appearing over an extended pawn (built, or the pawn moved under one): retract, with a message.
- Not standing (downed, lying, asleep): the stilts fold at once, no damage (my call; not discussed).
- Falling (user): a stilt removed while the stilts are at least 25% out (shot off, cut off) -> on the next tick:
  retract at once, 20 blunt damage split over 4 random outside parts, stun 180 ticks, message.
- Walking (user, 2026-11-18): +10% MoveSpeed while fully extended ("wider strides"). The controller's severity
  carries the state (0.5 otherwise, 1 extended; set by `SyncSeverity` every tick) and its extended stage has
  `statFactors MoveSpeed 1.1`, so the health system refreshes stats and the tooltip shows it. A pawn leaving the map
  (postfix on `Pawn.DeSpawn`) folds its stilts, so the bonus never travels with a caravan.
- Leg damage (user, same day: "50% more melee damage, ranged and blast and other sort does not count"): while
  extended, damage to a stilted leg (or a part below it) x `legDamageFactor` 1.5, only inside a melee cast. Scope =
  prefix/finalizer on vanilla `Verb_MeleeAttack.TryCastShot` (CE DLL: on `Verb_MeleeAttackCE.TryCastShot`, which
  overrides it); the multiplier is a prefix on `DamageWorker_AddInjury.FinalizeAndAddInjury(Pawn, float,
  DamageInfo, DamageResult)`, after armour, once per injured part (signature confirmed in CE's
  Harmony_DamageWorker_AddInjury.cs). CE pole hits are ranged, so normal.
- Save/load: state, transition start, auto flag, pending fall saved. Comps register in `Stilts` (pawn -> comp
  dictionary) on add and after load (postfix on `HediffSet.ExposeData`, PostLoadInit, once pawns are assigned).

## 3. Drawing (`Drawing.cs`)

- Prefix on `PawnRenderer.DynamicDrawPhaseAt`: the draw location moves `lift x fraction` cells north (2.43 at full
  height: 1 CE height unit = 1.75 m = 1 cell up the screen, like a tree). Every phase gets it, so body, head,
  apparel and weapon move together; label, health bar and selection stay at the feet.
- In the Draw phase two poles are drawn from the feet (0.34 below the cell centre) to the hips (0.2 below the lifted
  centre): three telescopic segments each (`StiltTube.png` stretched, widths 0.13 / 0.11 / 0.09), foot pads
  (`StiltFoot.png`). Facing east/west the poles overlap.

## 4. Range (`RangeStatPart.cs`, `Patches/Stats.xml`)

- `StatPart_StiltsRange` on `RangedWeapon_RangeMultiplier` (weapon stat): x (1 + setting, default 0.25) while the
  holder is extended. Vanilla's range and CE's `Verb_LaunchProjectileCE.EffectiveRange` (:148) both read it.

## 5. Cover (`Cover.cs`, both modes)

- `CoverUtility.CalculateOverallBlockChance` -> 0 inside an extended shooter's hit report or target scoring scope,
  and for an extended target (too tall to hide).
- `ShotReport.HitReportFor`: scope for extended shooters, and postfix zeroing `coversOverallBlockChance` when either
  end is extended (so shots never "miss into cover").
- Target scoring scope: vanilla `AttackTargetFinder.GetShootingTargetScore` (CE's non-snap finder in the CE DLL).

## 6. Over the wall: Elevated Gun Platform's logic, separate code (user: "use EGP as a check, same logic")

### 6.1 With Combat Extended (CE DLL, ported from EGP DESIGN 3.1-3.6)

- Heights: postfix on `CollisionVertical.CalculateHeightRange`: extended pawn spans (lift, lift + body) =
  (2.43, 3.43) for a human, shot height lift + 0.85 x body = 3.28. Overrides CE's 2.0 cap on humans. Crouching
  doesn't apply up there.
- Line of fire: prefix on `Verb_LaunchProjectileCE.CanHitCellFromCellIgnoringRange` when the caster or the target
  is extended (not airborne targets): EGP's copy of CE's routine with per-cell min line height against cover, roofs
  blocking where the line is below the slab top (every cell, both ends included), friendlies only when crossed;
  plus: an extended shooter skips partial cover. Without this, CE's own check (cover below the shooter's height is
  skipped) would let a 3.28 muzzle fire past every wall. `__runOriginal` respected (EGP may answer first).
- Sight: postfix on `AttackTargetFinder.CanSee`: EGP's 3D test (muzzle -> target top; gas, roofs, buildings by CE
  bounds; pawns and plants don't block).
- Aim (`GetTargetHeight`): prefix drops partial cover for an extended shooter; postfix maps CE's aim window
  (fractions of the top) onto the lifted body of an extended target; then EGP's aim raise over full-height obstacles
  and roof slabs, for either end extended (a climbing shot may pass an obstacle taller than the muzzle).
- Rounds fired from extended stilts (live check on the launcher; fragments excluded): roof first in roofed cells
  (EGP P2), never hit pawns in roofed cells (EGP P3), pass partial cover that is not their target (plants keep CE's
  rules); suppression with EGP's roof rule and above CE's height gate 3.
- Poles: below the lifted body there is no hitbox. A round whose path segment crosses the pole box (pawn width x
  lift) hits a pole with the setting's chance (0.15), rolled once per round and pawn (postfix on `CanCollideWith`).
- Body regions: inside `BulletCE.Impact` on an extended pawn, `GetCollisionBodyHeight` measures on the lifted body;
  below 45% of it (pole hits included) = Bottom = the legs = the stilts.

### 6.2 Without Combat Extended (main DLL)

- Same 3D test with CE's default heights fixed (`VanillaHeights`): wall/full fillage 2.0 (open door 0), partial
  things fillPercent, humanlike body 1.0 (others BodySize clamped 0.2-2), gun 0.85 of body, extended pawn +2.43;
  roof slab tops: built 2.1, thin rock 4.0, overhead mountain 8.0 (EGP plain-roofs rule). Cells walked exactly
  (`Line3D.CellsCrossed`).
- `AttackTargetFinder.CanSee` postfix (sight), `Verb.TryFindShootLineFromTo` postfix for direct-fire
  `Verb_LaunchProjectile` verbs (from the cell, then vanilla's lean cells; range checked first), and a prefix on
  private `Projectile.CheckForFreeIntercept(IntVec3)`: a round fired by or at an extended pawn passes any cell where
  its line (muzzle -> target top) is above everything standing there.
- Vanilla flight facts behind that patch, from mod sources (no game code read): `CheckForFreeInterceptBetween`
  walks the path in steps and calls private `CheckForFreeIntercept(IntVec3)` per cell; for each thing `CanHit`
  allows, a pawn gets a stance-based chance, a full-fillage thing other than an open door is hit for certain
  ("int-wall"), anything else by fillPercent. Sources: SCV Ready (workshop 3354842892) `Projectile_Homing.cs`
  re-implements that loop; Vehicle Framework release/1.6 `Patch_Combat.cs` transpiles `CheckForFreeIntercept` by name
  and binds the fields `origin`/`destination`; SCV also uses `intendedTarget` as a LocalTargetInfo field. So
  without the patch an extended pawn's rounds would stop in the first wall they fly over.
- Partial cover never blocks sight or fire in vanilla; cover is handled in section 5.

## 7. Settings

Range bonus (0.25), CE pole-hit chance (0.15), dev-mode debug overlay (flashes line cells with the line height).

## 8. Not done / later

- Enemy AI never extends; NPCs never spawn with stilts (no techHediffsTags).
- Removing stilts from prisoners reports no violation.
- Melee attacks by the pawn itself unchanged when extended.
