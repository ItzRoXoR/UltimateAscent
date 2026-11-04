using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>A look at the game's Venus fly traps (for the clown chest's fly trap built from one).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The game's Venus fly traps: how many the map has and where (scene objects, by biome and part of the map), any
    /// held as assets, and one of them part by part (where each part sits, its components: renderers, the animator's
    /// clips and their events, colliders and layers, sounds), with the plant's settings and where it drags a scout to.
    /// </summary>
    private IEnumerator FlyTrapProbe()
    {
        yield return null;
        VenusFlyTrap[] all = Resources.FindObjectsOfTypeAll<VenusFlyTrap>().Where(t => t != null).ToArray();
        VenusFlyTrap[] placed = all.Where(t => t.gameObject.scene.IsValid()).ToArray();
        VenusFlyTrap[] assets = all.Where(t => !t.gameObject.scene.IsValid()).ToArray();
        MapHandler map = Singleton<MapHandler>.Instance;
        int SegmentOf(Component c) => map != null ? System.Array.FindIndex(map.segments, s => s.segmentParent != null && c.transform.IsChildOf(s.segmentParent.transform)) : -1;
        Info("fly traps on the map", $"{placed.Length}: {string.Join(", ", placed.GroupBy(t => $"{BiomeOf(t)} in {(Segment)SegmentOf(t)}").Select(g => $"{g.Key} {g.Count()}"))}; "
            + $"as assets {assets.Length}: {string.Join(", ", assets.Select(a => a.name))}");
        Info("fly traps' places", string.Join("; ", placed.Take(12).Select(t => $"{PathOf(t.transform, null)} at {t.transform.position:F0}, scale {t.transform.lossyScale:F2}, active {t.gameObject.activeInHierarchy}")));
        VenusFlyTrap trap = placed.FirstOrDefault() ?? assets.FirstOrDefault();
        if (trap == null)
        {
            yield break;
        }
        Transform root = trap.transform;
        Info("the fly trap's settings",
            $"damage {trap.damage}, fall {trap.fallTime} s, extra drag {trap.extraDrag}, drag force {trap.dragForce}, cooldown {trap.cooldownTime} s, poison {trap.poisonDamage} every {trap.poisonFrequency} s; "
            + $"force curve [{string.Join(", ", trap.forceCurve.keys.Select(k => $"{k.time:0.##}:{k.value:0.##}"))}]; "
            + $"trap root '{(trap.trapRoot != null ? PathOf(trap.trapRoot, root) : "none")}' at {(trap.trapRoot != null ? root.InverseTransformPoint(trap.trapRoot.position).ToString("F2") : "-")}, "
            + $"drag to '{(trap.dragToPoint != null ? PathOf(trap.dragToPoint, root) : "none")}' at {(trap.dragToPoint != null ? root.InverseTransformPoint(trap.dragToPoint.position).ToString("F2") : "-")} (in the plant's space); "
            + $"view {(trap.view != null ? trap.view.ViewID.ToString() : "none")}, animator '{(trap.anim != null ? PathOf(trap.anim.transform, root) : "none")}'");
        Renderer[] looks = root.GetComponentsInChildren<Renderer>(true);
        if (looks.Length > 0)
        {
            Bounds bounds = looks[0].bounds;
            foreach (Renderer r in looks)
            {
                bounds.Encapsulate(r.bounds);
            }
            Info("the fly trap's size", $"{bounds.size:F2} m, its middle {root.InverseTransformPoint(bounds.center):F2} in the plant's space, the lowest {bounds.min.y - root.position.y:0.00} m and the highest {bounds.max.y - root.position.y:0.00} m from its root");
        }
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            List<string> what = new List<string>();
            foreach (Component c in part.GetComponents<Component>())
            {
                switch (c)
                {
                    case null:
                    case Transform _:
                        break;
                    case Renderer r:
                        what.Add($"{c.GetType().Name}(materials [{string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => $"'{m.name}' shader '{m.shader?.name}'"))}], enabled {r.enabled})");
                        break;
                    case Animator anim:
                        RuntimeAnimatorController controller = anim.runtimeAnimatorController;
                        what.Add(controller == null ? "Animator(no controller)" : $"Animator('{controller.name}', parameters [{string.Join(", ", anim.isInitialized ? anim.parameters.Select(p => $"{p.name}:{p.type}") : Enumerable.Empty<string>())}], clips [{string.Join("; ", controller.animationClips.Where(a => a != null).Select(a => $"{a.name} {a.length:0.##} s loop {a.isLooping} events [{string.Join(", ", a.events.Select(e => $"{e.functionName}@{e.time:0.##}({e.stringParameter}|{e.objectReferenceParameter?.name})"))}]"))}])");
                        break;
                    case Collider collider:
                        what.Add($"{c.GetType().Name}(trigger {collider.isTrigger}, layer {LayerMask.LayerToName(part.gameObject.layer)}, bounds {collider.bounds.size:F2}, enabled {collider.enabled})");
                        break;
                    case AudioSource audio:
                        what.Add($"AudioSource(clip '{audio.clip?.name}', volume {audio.volume:0.##}, play on awake {audio.playOnAwake}, loop {audio.loop})");
                        break;
                    case PhotonView view:
                        what.Add($"PhotonView(id {view.ViewID})");
                        break;
                    case Rigidbody body:
                        what.Add($"Rigidbody(kinematic {body.isKinematic}, mass {body.mass})");
                        break;
                    default:
                        what.Add(c.GetType().FullName);
                        break;
                }
            }
            Info($"fly trap part '{PathOf(part, root)}'", $"local {part.localPosition:F2}, rotation {part.localEulerAngles:F0}, scale {part.localScale:F2}, active {part.gameObject.activeSelf}; {string.Join("; ", what)}");
        }
        // Every serialized setting of its other scripts (its sounds and their clips among them).
        foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true).Where(s => s != null && !(s is VenusFlyTrap) && !(s is PhotonView)))
        {
            List<string> fields = new List<string>();
            foreach (System.Reflection.FieldInfo field in script.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                object value = field.GetValue(script);
                string shown = value is Object unity ? (unity != null ? $"'{unity.name}'" : "null") : value is System.Collections.IEnumerable list && !(value is string)
                    ? "[" + string.Join(", ", list.Cast<object>().Take(8).Select(o => o is Object u ? (u != null ? u.name : "null") : o?.ToString())) + "]" : value?.ToString();
                fields.Add($"{field.Name}={shown}");
            }
            Info($"fly trap script {script.GetType().Name} on '{PathOf(script.transform, root)}'", string.Join(", ", fields));
        }
        // A look at one in the part of the map the scout is in, if there is one.
        VenusFlyTrap near = placed.Where(t => t.gameObject.activeInHierarchy).OrderBy(t => Vector3.Distance(t.transform.position, Local.Center)).FirstOrDefault();
        if (near != null)
        {
            Vector3? stand = StandNear(near.transform.position + Vector3.up, 3f, 5f, seeIt: true) ?? StandNear(near.transform.position + Vector3.up, 3f, 7f, firm: false);
            if (stand.HasValue)
            {
                Local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand.Value + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(Local, near.transform.position + Vector3.up * 0.8f);
                yield return new WaitForSeconds(0.5f);
                Screenshot("flytrap-game");
            }
        }
    }
}
