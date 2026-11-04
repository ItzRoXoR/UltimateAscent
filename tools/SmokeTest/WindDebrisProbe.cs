using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>A look at what the Roots wind's debris is built from (debrisprobe, only when asked).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The parts the wind's debris is made of: the Stick's (firewood's) model, the cactus thorns a scout carries, the
    /// map's wind storms (their settings and anything they show), the Stone's look and the particle materials the far
    /// needles could use.
    /// </summary>
    private IEnumerator WindDebrisProbe()
    {
        yield return null;
        void Parts(string what, Transform root)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                List<string> found = new List<string>();
                foreach (Component c in part.GetComponents<Component>())
                {
                    switch (c)
                    {
                        case null:
                        case Transform _:
                            break;
                        case MeshFilter filter:
                            found.Add($"Mesh '{filter.sharedMesh?.name}' bounds {filter.sharedMesh?.bounds.size:F3} centre {filter.sharedMesh?.bounds.center:F3}, {filter.sharedMesh?.vertexCount} verts");
                            break;
                        case Renderer r:
                            found.Add($"{c.GetType().Name}(enabled {r.enabled}, materials [{string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => $"'{m.name}' shader '{m.shader?.name}' tex '{(m.HasProperty("_BaseTexture") ? m.GetTexture("_BaseTexture")?.name : m.mainTexture?.name)}'"))}])");
                            break;
                        case Collider collider:
                            found.Add($"{c.GetType().Name}(trigger {collider.isTrigger}, layer {LayerMask.LayerToName(part.gameObject.layer)})");
                            break;
                        case ParticleSystem ps:
                            ParticleSystem.MainModule main = ps.main;
                            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                            Material m2 = psr != null ? psr.sharedMaterial : null;
                            List<string> props = new List<string>();
                            if (m2 != null && m2.shader != null)
                            {
                                for (int i = 0; i < m2.shader.GetPropertyCount(); i++)
                                {
                                    props.Add(m2.shader.GetPropertyName(i));
                                }
                            }
                            found.Add($"ParticleSystem(max {main.maxParticles}, rate {ps.emission.rateOverTimeMultiplier:0.#}, life {main.startLifetimeMultiplier:0.##}, size {main.startSizeMultiplier:0.###}, speed {main.startSpeedMultiplier:0.#}, space {main.simulationSpace}, render {psr?.renderMode}, "
                                + $"material '{m2?.name}' shader '{m2?.shader?.name}' tex '{m2?.mainTexture?.name}' props [{string.Join(" ", props)}])");
                            break;
                        default:
                            found.Add(c.GetType().Name);
                            break;
                    }
                }
                Info($"{what} part '{PathOf(part, root)}'", $"local {part.localPosition:F3} rot {part.localEulerAngles:F0} scale {part.localScale:F3}, active {part.gameObject.activeSelf}; {string.Join("; ", found)}");
            }
        }
        if (ItemDatabase.TryGetItem(StickItem.PrefabName, out Item stick) && stick != null)
        {
            Info("the Stick", $"defaultPos {stick.defaultPos:F2}, forward {stick.defaultForward:F2}, mass {stick.GetComponent<Rigidbody>()?.mass}");
            Parts("Stick", stick.transform);
        }
        if (StoneItem.Template != null)
        {
            Parts("Stone", StoneItem.Template.transform);
        }
        // The thorns a scout carries, by type: how many and what one looks like.
        CharacterAfflictions afflictions = Local.refs.afflictions;
        Info("the scout's thorns", string.Join(", ", afflictions.physicalThorns.GroupBy(t => t.type).Select(g => $"type {g.Key}: {g.Count()}, damage {g.First().GetThornDamage()}, pops out {g.First().popsOutAutomatically} ({g.First().popOutTimeSolo} s solo)")));
        ThornOnMe cactus = afflictions.physicalThorns.FirstOrDefault(t => t.type == 0);
        if (cactus != null)
        {
            Parts("cactus thorn", cactus.transform);
            Info("a cactus thorn on the scout", $"on '{PathOf(cactus.transform, Local.transform)}', lossy scale {cactus.transform.lossyScale:F3}");
        }
        foreach (WindChillZone zone in Resources.FindObjectsOfTypeAll<WindChillZone>().Where(z => z != null && z.gameObject.scene.IsValid()))
        {
            Info($"wind zone '{PathOf(zone.transform, null)}'", $"active {zone.gameObject.activeInHierarchy}, bounds {zone.windZoneBounds.center:F0} size {zone.windZoneBounds.size:F0}, force {zone.windForce}, on {zone.windTimeRangeOn}, off {zone.windTimeRangeOff}, "
                + $"intensity curve {zone.useIntensityCurve}, raycast {zone.useRaycast} ({zone.minRaycastDistance}-{zone.maxRaycastDistance}), moves items {zone.windMovesItems}, status {zone.statusType} {zone.statusApplicationPerSecond}, wind now {zone.windActive}, direction {zone.currentWindDirection:F2}");
            Parts("wind zone", zone.transform);
        }
        GameObject dust = StoneItem.DustTemplate();
        if (dust != null)
        {
            Parts("stone dust", dust.transform);
        }
        // Particle materials in memory with their shaders (candidates for the far needles).
        Info("particle materials", string.Join("; ", Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>().Where(r => r != null && r.sharedMaterial != null)
            .Select(r => $"'{r.sharedMaterial.name}' ({r.sharedMaterial.shader?.name})").Distinct().Take(60)));
    }
}
