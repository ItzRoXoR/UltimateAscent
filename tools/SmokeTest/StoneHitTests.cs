using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Whom a Stone knocks down: only what it shatters on, but for its thrower, whom a whole one hits too.</summary>
internal sealed partial class Runner
{
    /// <summary>A throw charged halfway: a stone flies at about 10 m/s, too slow to shatter.</summary>
    private const float HalfThrow = 0.5f;

    /// <summary>The throw straight up: back down on the scout at 10 m/s or more (it knocks them down), under 18 (it stays whole).</summary>
    private const float UpThrow = 0.6f;

    /// <summary>
    /// A Stone thrown straight up (the game's throw, charged most of the way) comes back down on the scout who threw it
    /// without shattering: it knocks them down for 2 s and does not injure them. A look stops 5 degrees short of straight
    /// up, so the stone comes down a little ahead: the scout steps under it as it turns at the top (moved there).
    /// </summary>
    private IEnumerator StoneStraightUp(Item stone)
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        try
        {
            yield return SpawnIntoHand(stone);
            if (held == null)
            {
                yield break;
            }
            Protect(false);
            Afflictions.SetStatus(Status.Injury, 0f);
            local.data.lookValues = new Vector2(look.x, 85f);
            yield return new WaitForSeconds(0.6f);
            int hitsBefore = StoneItem.Hits;
            int injuringBefore = StoneItem.Injuring;
            int breaksBefore = StoneItem.Breaks;
            HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
            StoneContacts.Seen.Clear();
            Vector3 from = held.transform.position;
            Step("throw a Stone straight up", () =>
            {
                byte slot = local.refs.items.currentSelectedSlot.Value;
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, UpThrow, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
            });
            Item flying = null;
            float highest = from.y, longest = 0f, thrownAt = 0f;
            bool under = false;
            string stepped = "never stepped under it";
            for (float until = Time.time + 5f; Time.time < until;)
            {
                flying ??= Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == stone.itemID);
                if (flying != null && flying.rig != null)
                {
                    highest = Mathf.Max(highest, flying.transform.position.y);
                    thrownAt = Mathf.Max(thrownAt, flying.rig.linearVelocity.magnitude);
                    // At the top: where it comes down to the scout's head, and the scout there.
                    if (!under && flying.rig.linearVelocity.y <= 0f && flying.transform.position.y > local.Head.y + 1f)
                    {
                        under = true;
                        float drop = flying.transform.position.y - local.Head.y;
                        Vector3 sideways = Vector3.ProjectOnPlane(flying.rig.linearVelocity, Vector3.up);
                        Vector3 down = flying.transform.position + sideways * Mathf.Sqrt(2f * drop / -Physics.gravity.y);
                        if (Physics.Raycast(new Vector3(down.x, local.Center.y + 2f, down.z), Vector3.down, out RaycastHit floor, 6f, HelperFunctions.terrainMapMask))
                        {
                            stepped = $"stepped {Vector3.ProjectOnPlane(down - local.Head, Vector3.up).magnitude:0.0} m under it, {drop:0.0} m below it";
                            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, floor.point + Vector3.up * 1.2f, false);
                        }
                    }
                }
                longest = Mathf.Max(longest, local.data.fallSeconds);
                yield return null;
            }
            float injury = Afflictions.GetCurrentStatus(Status.Injury);
            List<(float together, float own, bool scout, bool hit)> onScout = StoneContacts.Seen.Where(c => c.scout).ToList();
            Check("a Stone thrown straight up comes back down on its thrower without shattering: it knocks them down for 2 s, no injury (9)",
                Mathf.Abs(longest - StoneItem.Knockdown) < 0.1f && injury < 0.001f && StoneItem.Hits > hitsBefore && StoneItem.Injuring == injuringBefore && StoneItem.Breaks == breaksBefore,
                $"thrown at {thrownAt:0.#} m/s, rose {highest - from.y:0.0} m, the scout {stepped}; on the scout {string.Join(", ", onScout.Select(c => $"{c.own:0.#} m/s, hit {c.hit}"))}; knocked down for {longest:0.00} s, {injury * 100f:0.#} injury, "
                + $"hits {StoneItem.Hits - hitsBefore} ({StoneItem.Injuring - injuringBefore} injuring), breaks {StoneItem.Breaks - breaksBefore}");
            if (flying != null && flying.photonView != null)
            {
                PhotonNetwork.Destroy(flying.gameObject);
            }
        }
        finally
        {
            Afflictions.SetStatus(Status.Injury, 0f);
            Protect(true);
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// Stones thrown at a ground snake lying in front of the scout (17, in the Tropics; the game's throw): half charged,
    /// the stone flies on whole and the snake is not stunned; full charge, the stone shatters on the snake and stuns it.
    /// </summary>
    private IEnumerator StoneAtSnake()
    {
        Character local = Local;
        Item stone = StoneItem.Template;
        if (!Snakes.On || stone == null)
        {
            Info("the Stone-on-snake checks are skipped", $"snakes {Snakes.On} (17), the Stone {stone != null}");
            yield break;
        }
        if (Map.CurrentPlace != Place.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Tropics)
        {
            Info("the Stone-on-snake checks are skipped", $"no Tropics here ({Map.CurrentPlace}); run with -Biome and a map number that has the Tropics");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && Snakes.All.Count == 0;)
        {
            yield return null;
        }
        List<Snake> snakes = Snakes.All.Where(s => s != null).ToList();
        Snake snake = snakes.Where(s => !s.IsTree && AboveGround(s.Head) < 0.5f).OrderBy(s => Vector3.Distance(s.Head, local.Center)).FirstOrDefault();
        if (snake == null)
        {
            Info("the Stone-on-snake checks are skipped", "no ground snake on the ground");
            yield break;
        }
        foreach (Snake other in snakes.Where(s => s != snake))
        {
            other.Ignore();
        }
        Protect(true);
        Vector2 look = local.data.lookValues;
        try
        {
            foreach ((float charge, bool stuns) in new[] { (HalfThrow, false), (1f, true) })
            {
                // On open, flat ground (by the scout, else by the snake): the snake lies there facing the scout, who stands
                // 2.5 m in front of it (it notices nobody until the stone).
                Vector3? stage = null;
                Vector3 way = Vector3.forward;
                foreach (Vector3 near in new[] { local.Center, snake.Head })
                {
                    for (float distance = 2f; distance <= 14f && !stage.HasValue; distance += 3f)
                    {
                        for (int angle = 0; angle < 360 && !stage.HasValue; angle += 30)
                        {
                            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                            Vector3? centre = FlatOpen(near + dir * distance);
                            if (centre.HasValue && FlatOpen(centre.Value - dir * 2.5f).HasValue && FlatOpen(centre.Value + dir).HasValue && FlatOpen(centre.Value + dir * 2f).HasValue)
                            {
                                stage = centre;
                                way = dir;
                            }
                        }
                    }
                }
                if (!stage.HasValue)
                {
                    Info("the Stone-on-snake checks are skipped", $"no open, flat ground by the scout at {local.Center:F0} or the snake at {snake.Head:F0}");
                    yield break;
                }
                snake.Unaware = true;
                snake.Puppet = true;
                snake.PlaceAt(stage.Value + Vector3.up * 0.06f, stage.Value - way);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stage.Value - way * 2.5f + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                yield return SpawnIntoHand(stone);
                if (held == null)
                {
                    yield break;
                }
                // Aimed from the hand (where the stone leaves) at the middle of the snake's hit box; the hand moves with the look.
                for (int aim = 0; aim < 3; aim++)
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(snake.transform.position - held.transform.position);
                    yield return new WaitForSeconds(0.25f);
                }
                snake.Puppet = false;
                int breaksBefore = StoneItem.Breaks;
                HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                Vector3 snakeAt = snake.transform.position;
                Step($"throw a Stone {(stuns ? "hard" : "half charged")} at a snake", () =>
                {
                    byte slot = local.refs.items.currentSelectedSlot.Value;
                    local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, charge, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
                });
                Item flying = null;
                float nearest = float.MaxValue, fastest = 0f;
                bool stunned = false;
                for (float until = Time.time + 1.5f; Time.time < until;)
                {
                    flying ??= Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == stone.itemID);
                    if (flying != null && flying.rig != null)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(flying.transform.position, snakeAt));
                        fastest = Mathf.Max(fastest, flying.rig.linearVelocity.magnitude);
                    }
                    stunned |= snake.Doing == "Stunned";
                    yield return null;
                }
                bool shattered = StoneItem.Breaks > breaksBefore;
                string seen = $"the stone flew at up to {fastest:0.#} m/s and came within {nearest:0.00} m of the snake; the snake {(stunned ? "stunned" : "not stunned")}, the stone {(shattered ? "shattered" : "whole")}";
                if (stuns)
                {
                    Check("a Stone thrown hard at a snake shatters on it and stuns it (17)", stunned && shattered, seen);
                }
                else
                {
                    Check("a Stone thrown half charged at a snake flies on whole and does not stun it (17)", !stunned && !shattered && nearest < 0.6f, seen);
                }
                if (flying != null && flying.photonView != null)
                {
                    PhotonNetwork.Destroy(flying.gameObject);
                }
                // Its stun over before it is staged again.
                for (float until = Time.time + 4f; Time.time < until && snake.Doing == "Stunned";)
                {
                    yield return null;
                }
            }
        }
        finally
        {
            snake.Puppet = false;
            snake.Unaware = false;
            local.data.lookValues = look;
        }
    }
}
