# Stilts Bionics: in-game checks

2026-11-18: tested in game by the author after commit efa33f7: works (no failures reported; which items below were
covered was not itemised).
Turn on the dev-mode debug overlay in the mod settings for the line checks (red = blocked, green = clear, number =
the line's height; a wall is 2.0).

## 1. Load

1. Start with and without Combat Extended: no red errors; no "[Stilts Bionics] patch ... skipped" warnings. Any such
   warning names a vanilla/CE method that differs in this version: report it.
2. With CE: `CE/Assemblies/StiltsBionicsCE.dll` loads (no warnings starting "[Stilts Bionics CE]").

## 2. Item and surgery

1. Bionics researched: "bionic stilts" at the fabrication bench, 40 plasteel + 10 advanced components.
2. Health tab > Operations: "install bionic stilts" appears once (no body part choice). After surgery both legs show
   "bionic stilt" and the whole body shows "telescopic stilts (retracted)". Old bionic legs drop as items.
3. Operations no longer offer "remove leg" or "install bionic leg" on a stilted leg.
4. "remove bionic stilts": both legs end missing, the pair item drops. With one stilt shot off first: no item.
5. Moving speed retracted: like bionic legs.

## 3. Extend and retract

1. Gizmo "Extend stilts": 2 s standing still, the pawn rises with two telescopic poles; then "Retract stilts".
2. Disabled under a roof, with one stilt missing, when downed.
3. During the 2 s: can't move, can't shoot.
4. Draw check: body, head, apparel and weapon all lifted together, poles from the feet to the hips, east/west view
   poles overlapping; label/health bar/selection at the feet. Tune `FootBelow`/`HipBelow` in Drawing.cs if off.
5. Save and load while extended and mid-transition: state kept, gizmo works.

## 4. Roofs

1. Order an extended pawn into a roofed building: it stops at the edge, retracts, walks in.
2. Drafted: order it out again; it extends by itself once it stops outside. Undrafted: stays retracted.
3. Unroofed gate/door in a wall: walks through extended.
4. Build a roof over an extended pawn: it retracts with a message.
5. Sleeping/lying down outside: stilts fold at once.

## 5. Falling

1. Dev tools: destroy a stilt (or shoot one off) while extended: "fell six metres", ~20 blunt damage over a few parts,
   3 s stun, stilts retracted.

## 6. Range and cover

1. Weapon range ring larger by 25% when extended (both modes). Setting changes it.
2. Vanilla: hit chance tooltip shows no cover on targets behind sandbags; an extended pawn targeted by enemies gets no
   cover either.

## 7. Over the wall

Setup: extended pawn 2 cells behind its own wall, raider 20 cells away in the open; second raider standing right
behind a wall far away.
1. The extended pawn targets and hits the first raider over its own wall; the first raider can shoot back.
2. Neither can target the other across the wall hugging the second raider.
3. A raider under a roof (or inside a roofed building with an open door) can't be targeted, and can't shoot back.
4. Vanilla only: the extended pawn's rounds don't stop in its own wall. If they do, look for the warning
   "Projectile.CheckForFreeIntercept or its fields not found". (That vanilla rounds stop at full-fillage things in
   flight is confirmed from two mods' sources; see DESIGN 6.2.) With dev mode "draw shooting", vanilla throws the
   text "int-wall" where a round hits a wall.
5. CE only: aim raise: rounds clear a wall partway along the line instead of hitting it; rounds pass sandbags next
   to the target; extended pawn hit mostly in the body; some rounds (about 15% of those crossing the stilt zone) hit
   a stilt; a stilt shot off -> fall.
6. CE + Elevated Gun Platform: an elevated turret and an extended pawn can engage each other (no errors).
