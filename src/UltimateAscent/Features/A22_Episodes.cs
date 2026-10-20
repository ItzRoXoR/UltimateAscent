using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 22's episodes. Each comes at random, on average once every 30 s with that status filling the bar and less
/// often the less of it there is (half a bar: every minute), never below 10 of it. Each player's game runs its own
/// scout's; what others see and hear goes to every game.
/// <list type="bullet">
/// <item>Spores: 20 s of numbness (the stamina bar hidden), and 3 s in the game's spore burst around the scout, weaker
/// than a Shroomberry's: the scout is tossed up half as hard, and scouts within 5 m are knocked down for up to 1 s and
/// pushed back a little, and get up to 5 spores.</item>
/// <item>Cold (its slowing stays, see <see cref="A22_UniqueAilments"/>): from 70 of it, the scout is now and then
/// knocked down for 5 s, shivering where they land.</item>
/// <item>Heat: dizziness for 6 s. The view closes in dark from the edges and sways, both fading in and out over 1.5 s,
/// and stamina comes back half as fast meanwhile. Only that scout's player sees it.</item>
/// <item>Poison: eating while poisoned may bring nausea (never by itself): the likelier the more poison (25: one time
/// in two; 50 or more: every time). That food restores no hunger, and nothing eaten in the next 3 s does; the scout
/// stands bent over, hands on the knees and head down, for 3 s while their stomach growls (heard by everyone near).</item>
/// </list>
/// </summary>
internal static class A22_Episodes
{
    internal const float FullBarEvery = 30f;
    internal const float Threshold = 0.1f;

    internal const float NumbSeconds = 20f;
    internal const float BurstDelay = 3f;
    internal const float BurstRange = 5f;
    internal const float BurstKnockdown = 1f;
    internal const float BurstSpores = 0.05f;
    private const float BurstPush = 600f;

    /// <summary>The game's spore burst (a Shroomberry's), its look only: what it does is this burst's own, weaker.</summary>
    private const string BurstLook = "VFX_SporeExploExploEdibleSpawn_NoKnockback";

    /// <summary>The scout the burst comes from is tossed up half as hard as by a Shroomberry's (100).</summary>
    private const float BurstToss = 50f;

    internal const float ColdFrom = 0.7f;
    internal const float ColdKnockdown = 5f;
    private const float ShiverEvery = 0.07f;
    private const float ShiverForce = 2.5f;

    /// <summary>The fall is left to play out (up to this long) before the body is held where it landed.</summary>
    private const float LandingAtMost = 1.5f;

    internal const float DizzySeconds = 6f;
    internal const float DizzyFade = 1.5f;
    internal const float DizzyRegen = 0.5f;
    /// <summary>The dark at the middle of the view and at its edges, at the spell's height.</summary>
    private const float DizzyDarkMiddle = 0.6f;
    private const float DizzyDarkEdge = 0.97f;
    private const float DizzyRoll = 4f;
    private const float DizzyYaw = 2f;
    private const float DizzyPitch = 1.5f;

    internal const float NauseaSeconds = 3f;
    internal const float NauseaFullAt = 0.5f;
    private const string NauseaResource = "UltimateAscent.Nausea_stomach.wav";
    private const float NauseaVolume = 0.6f;

    private const string BurstMessage = "a22-spore-burst";
    private const string NauseaMessage = "a22-nausea";

    private static bool On => Rules.Enabled(Lvl.UniqueAilments);

    private static float sporesReady;
    private static float coldReady;
    private static float dizzyStarted = -100f;
    private static float nauseaUntil = -100f;
    private static float shiverUntil = -100f;
    private static SFX_Instance stomach;
    private static bool stomachFailed;
    private static GameObject burstTemplate;
    private static GameObject dizzyCanvas;
    private static UnityEngine.UI.RawImage dizzyOverlay;

    /// <summary>Scouts bent over with nausea on this game, until when (by their view).</summary>
    private static readonly Dictionary<int, float> bentOver = new Dictionary<int, float>();

    /// <summary>For the tests.</summary>
    internal static int Bursts, Nauseas, StomachsHeard, ColdKnockdowns, Dizzies;

    /// <summary>For the tests: how dark the middle of the view is now, and what the last burst's look was made of.</summary>
    internal static float Darkness;
    internal static string BurstMadeOf = "";

    internal static void Reset()
    {
        sporesReady = coldReady = 0f;
        dizzyStarted = nauseaUntil = shiverUntil = -100f;
        bentOver.Clear();
    }

    internal static void Listen()
    {
        Net.Handle(BurstMessage, args =>
        {
            if (args.Length > 1 && args[0] is Vector3 at && args[1] is int source)
            {
                Burst(at, source);
            }
        });
        Net.Handle(NauseaMessage, args =>
        {
            if (args.Length > 0 && args[0] is int view)
            {
                Nausea(view);
            }
        });
    }

    /// <summary>The local scout's episodes; every frame.</summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        DarkenView(local);
        if (!On || local == null || local.data.dead || local.data.fullyPassedOut || !Map.Ready)
        {
            return;
        }
        CharacterAfflictions afflictions = local.refs.afflictions;
        float dt = Time.deltaTime;
        float spores = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Spores);
        if (spores >= Threshold && Time.time >= sporesReady && UnityEngine.Random.value < dt * spores / FullBarEvery)
        {
            StartSpores(local);
        }
        float cold = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold);
        if (cold >= ColdFrom && Time.time >= coldReady && UnityEngine.Random.value < dt * cold / FullBarEvery)
        {
            StartCold(local);
        }
        float heat = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hot);
        if (heat >= Threshold && Time.time >= dizzyStarted + DizzySeconds && UnityEngine.Random.value < dt * heat / FullBarEvery)
        {
            StartDizziness();
        }
    }

    // ------------------------------------------------------------------ spores

    internal static void StartSpores(Character local)
    {
        sporesReady = Time.time + NumbSeconds;
        local.refs.afflictions.AddAffliction(new Affliction_Numb { totalTime = NumbSeconds });
        local.StartCoroutine(BurstLater(local));
    }

    private static IEnumerator BurstLater(Character local)
    {
        yield return new WaitForSeconds(BurstDelay);
        if (local != null && !local.data.dead)
        {
            Net.SendAll(BurstMessage, local.Center, local.photonView.ViewID);
            local.AddForceToBodyPart(local.GetBodypartRig(BodypartType.Hip), Vector3.zero, Vector3.up * BurstToss);
        }
    }

    /// <summary>On every game: the burst seen and heard, and this game's scout knocked down, pushed and spored if near (not the one it came from).</summary>
    private static void Burst(Vector3 at, int source)
    {
        Bursts++;
        GameObject look = BurstTemplate();
        if (look != null)
        {
            GameObject made = UnityEngine.Object.Instantiate(look, at, Quaternion.identity);
            made.SetActive(true);
        }
        Character local = Character.localCharacter;
        if (local == null || local.data.dead || local.photonView == null || local.photonView.ViewID == source)
        {
            return;
        }
        float distance = Vector3.Distance(local.Center, at);
        if (distance > BurstRange)
        {
            return;
        }
        float strength = 1f - distance / BurstRange;
        Vector3 away = local.Center - at;
        away = (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) + Vector3.up * 0.4f;
        local.Fall(BurstKnockdown * strength);
        local.AddForce(away.normalized * BurstPush * strength);
        ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Spores, BurstSpores * strength, "spore burst");
    }

    /// <summary>
    /// The game's spore burst kept aside with what it does to scouts switched off (its blast reaching nobody), so that
    /// only its cloud and sound are left.
    /// </summary>
    private static GameObject BurstTemplate()
    {
        if (burstTemplate != null)
        {
            return burstTemplate;
        }
        GameObject prefab = Resources.Load<GameObject>(BurstLook);
        if (prefab == null)
        {
            return null;
        }
        GameObject template = UnityEngine.Object.Instantiate(prefab);
        template.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(template);
        template.name = "UltimateAscent Spore Burst";
        List<string> parts = new List<string>();
        foreach (Component component in template.GetComponentsInChildren<Component>(true))
        {
            if (component is AOE blast)
            {
                blast.range = 0f;
            }
            if (component != null && !(component is Transform))
            {
                parts.Add(component.GetType().Name);
            }
        }
        BurstMadeOf = string.Join(", ", parts);
        burstTemplate = template;
        return burstTemplate;
    }

    // ------------------------------------------------------------------ cold

    internal static void StartCold(Character local)
    {
        coldReady = Time.time + ColdKnockdown + 2f;
        shiverUntil = Time.time + ColdKnockdown;
        ColdKnockdowns++;
        local.Fall(ColdKnockdown);
        local.StartCoroutine(Shiver(local));
    }

    /// <summary>
    /// While knocked down by the cold the limp body trembles: quick small jolts, side to side, on the chest, head and
    /// arms. The fall plays out first; from where the scout lands, the body as a whole is kept from sliding off, so it
    /// shivers in place.
    /// </summary>
    private static IEnumerator Shiver(Character local)
    {
        BodypartType[] parts = { BodypartType.Torso, BodypartType.Head, BodypartType.Arm_L, BodypartType.Arm_R, BodypartType.Elbow_L, BodypartType.Elbow_R };
        List<Rigidbody> body = new List<Rigidbody>();
        foreach (Bodypart part in local.refs.ragdoll.partList)
        {
            if (part != null && part.Rig != null)
            {
                body.Add(part.Rig);
            }
        }
        for (float landingBy = Time.time + LandingAtMost; Time.time < landingBy && Time.time < shiverUntil;)
        {
            yield return new WaitForFixedUpdate();
            if (Time.time > landingBy - LandingAtMost + 0.4f && Moving(body).magnitude < 0.5f)
            {
                break;
            }
        }
        float sign = 1f;
        float push = 0.5f; // the first jolt is half, so the jolts swing about the body's place
        float nextJolt = 0f;
        while (local != null && Time.time < shiverUntil && !local.data.dead)
        {
            yield return new WaitForFixedUpdate();
            Vector3 drift = Moving(body);
            foreach (Rigidbody rig in body)
            {
                if (!rig.isKinematic)
                {
                    rig.AddForce(-drift, ForceMode.VelocityChange);
                }
            }
            if (Time.time < nextJolt)
            {
                continue;
            }
            nextJolt = Time.time + ShiverEvery;
            Vector3 side = local.GetBodypart(BodypartType.Hip).transform.right;
            foreach (BodypartType type in parts)
            {
                Rigidbody rig = local.GetBodypartRig(type);
                if (rig != null && !rig.isKinematic)
                {
                    rig.AddForce((side * sign + UnityEngine.Random.insideUnitSphere * 0.3f) * ShiverForce * push, ForceMode.VelocityChange);
                }
            }
            sign = -sign;
            push = 1f;
        }
    }

    /// <summary>How the body as a whole is moving: its parts' average velocity, along the ground only.</summary>
    private static Vector3 Moving(List<Rigidbody> body)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Rigidbody rig in body)
        {
            if (rig != null && !rig.isKinematic)
            {
                sum += rig.linearVelocity;
                count++;
            }
        }
        Vector3 mean = count > 0 ? sum / count : Vector3.zero;
        mean.y = 0f;
        return mean;
    }

    // ------------------------------------------------------------------ heat

    internal static void StartDizziness()
    {
        dizzyStarted = Time.time;
        Dizzies++;
    }

    /// <summary>How strong the dizziness is now: fading in, held, fading out (0 outside an episode).</summary>
    internal static float Dizziness
    {
        get
        {
            float t = Time.time - dizzyStarted;
            if (t < 0f || t > DizzySeconds || !On)
            {
                return 0f;
            }
            float fadeIn = Mathf.Clamp01(t / DizzyFade);
            float fadeOut = Mathf.Clamp01((DizzySeconds - t) / DizzyFade);
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
        }
    }

    /// <summary>
    /// The dizzy dark: the view closes in from its edges, darkest at the rim, breathing a little with the sway. It lies
    /// over the world but under the game's own HUD.
    /// </summary>
    private static void DarkenView(Character local)
    {
        float dizzy = local != null && !local.data.dead && !local.data.fullyPassedOut ? Dizziness : 0f;
        Darkness = dizzy * DizzyDarkMiddle;
        if (dizzy <= 0f)
        {
            if (dizzyCanvas != null && dizzyCanvas.activeSelf)
            {
                dizzyCanvas.SetActive(false);
            }
            return;
        }
        if (dizzyCanvas == null && !MakeOverlay())
        {
            return;
        }
        dizzyCanvas.SetActive(true);
        float breathing = 1f + Mathf.Sin(Time.time * 1.3f) * 0.04f;
        dizzyOverlay.color = new Color(0f, 0f, 0f, Mathf.Clamp01(dizzy * breathing));
        dizzyOverlay.rectTransform.localScale = Vector3.one * (1f + (1f - dizzy) * 0.6f);
    }

    private static bool MakeOverlay()
    {
        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UltimateAscent Dizzy Dark" };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2f);
                float alpha = Mathf.Lerp(DizzyDarkMiddle, DizzyDarkEdge, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, r)));
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        dizzyCanvas = new GameObject("UltimateAscent Dizzy Dark");
        UnityEngine.Object.DontDestroyOnLoad(dizzyCanvas);
        Canvas canvas = dizzyCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -100;
        GameObject image = new GameObject("Dark", typeof(RectTransform));
        image.transform.SetParent(dizzyCanvas.transform, false);
        dizzyOverlay = image.AddComponent<UnityEngine.UI.RawImage>();
        dizzyOverlay.texture = texture;
        dizzyOverlay.raycastTarget = false;
        RectTransform rect = dizzyOverlay.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return true;
    }

    /// <summary>The view sways, slowly and smoothly, after the game has placed the camera (the scout's own look is untouched).</summary>
    [HarmonyPatch(typeof(MainCameraMovement), "LateUpdate")]
    private static class DizzySway
    {
        private static void Postfix(MainCameraMovement __instance)
        {
            float dizzy = Dizziness;
            Character local = Character.localCharacter;
            if (dizzy <= 0f || local == null || local.data.fullyPassedOut)
            {
                return;
            }
            float t = Time.time;
            Quaternion sway = Quaternion.Euler(Mathf.Sin(t * 0.7f + 1f) * DizzyPitch * dizzy, Mathf.Sin(t * 0.5f) * DizzyYaw * dizzy, Mathf.Sin(t * 0.9f) * DizzyRoll * dizzy);
            __instance.transform.rotation *= sway;
        }
    }

    /// <summary>Stamina comes back at half the pace during a dizzy spell (the game's regaining, on the scout's own game).</summary>
    [HarmonyPatch(typeof(Character), "UpdateVariablesFixed")]
    private static class SlowerStamina
    {
        [ThreadStatic] internal static Character updating;

        private static void Prefix(Character __instance) => updating = __instance;

        private static Exception Finalizer(Exception __exception)
        {
            updating = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
    private static class HalfRegaining
    {
        private static void Prefix(Character __instance, ref float add)
        {
            if (add > 0f && SlowerStamina.updating == __instance && __instance.IsLocal && Dizziness > 0f)
            {
                add *= DizzyRegen;
            }
        }
    }

    // ------------------------------------------------------------------ poison

    internal static bool Nauseous => On && Time.time < nauseaUntil;

    /// <summary>The meal (one item use) the nausea roll was made for, so each meal rolls once.</summary>
    private static Item rolledFor;
    private static float rolledAt;
    private static bool rolledSick;

    /// <summary>Whether this food gives its hunger: not during nausea, nor when eating it brings nausea on.</summary>
    private static bool Feeds(Character character, Item item)
    {
        if (!On || character == null || !character.IsLocal)
        {
            return true;
        }
        if (Nauseous)
        {
            return false;
        }
        float poison = character.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Poison);
        if (poison <= 0.02f)
        {
            return true;
        }
        if (item != rolledFor || Time.time - rolledAt > 1f)
        {
            rolledFor = item;
            rolledAt = Time.time;
            rolledSick = UnityEngine.Random.value < Mathf.Clamp01(poison / NauseaFullAt);
            if (rolledSick)
            {
                nauseaUntil = Time.time + NauseaSeconds;
                Net.SendAll(NauseaMessage, character.photonView.ViewID);
                return false;
            }
        }
        return !rolledSick;
    }

    [HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static class NoFoodWhenSick
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Action_RestoreHunger __instance) => Feeds(__instance.character, __instance.item);
    }

    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class NoHungerFromStatusWhenSick
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Action_ModifyStatus __instance)
        {
            if (__instance.statusType != CharacterAfflictions.STATUSTYPE.Hunger || __instance.changeAmount >= 0f)
            {
                return true;
            }
            Character character = __instance.item != null ? __instance.item.holderCharacter : null;
            return Feeds(character, __instance.item);
        }
    }

    /// <summary>On every game: that scout bends over for 3 s, and their stomach growls.</summary>
    private static void Nausea(int view)
    {
        Nauseas++;
        bentOver[view] = Time.time + NauseaSeconds;
        Photon.Pun.PhotonView photonView = Photon.Pun.PhotonNetwork.GetPhotonView(view);
        Character scout = photonView != null ? photonView.GetComponent<Character>() : null;
        SFX_Instance sound = StomachSound();
        if (scout != null && sound != null)
        {
            sound.Play(scout.Center);
            StomachsHeard++;
        }
    }

    internal static bool BentOver(Character character) => On && character != null && character.photonView != null
        && bentOver.TryGetValue(character.photonView.ViewID, out float until) && Time.time < until;

    /// <summary>A nauseous scout does not walk, run or jump (looking around and using items still work).</summary>
    [HarmonyPatch(typeof(CharacterInput), nameof(CharacterInput.Sample))]
    private static class StandsStill
    {
        private static void Postfix(CharacterInput __instance)
        {
            if (Nauseous)
            {
                __instance.movementInput = Vector2.zero;
                __instance.sprintIsPressed = false;
                __instance.sprintWasPressed = false;
                __instance.jumpWasPressed = false;
                __instance.jumpIsPressed = false;
            }
        }
    }

    /// <summary>
    /// The bent-over pose, laid on the game's own animation just before each part of the body takes it as the pose it
    /// pulls towards: bent at the waist and chest, the head hanging, the arms reaching down to the knees; the legs keep
    /// the game's stance, so the feet stay on the ground. Turned about the scout's own left-right axis, so it does not
    /// depend on how each bone is set up (each part turns with the ones above it).
    /// </summary>
    [HarmonyPatch(typeof(Bodypart), nameof(Bodypart.SaveAnimationData))]
    private static class BendsOver
    {
        private static void Prefix(Bodypart __instance)
        {
            Character character = __instance.character;
            if (!BentOver(character))
            {
                return;
            }
            float angle;
            switch (__instance.partType)
            {
                case BodypartType.Mid: angle = 30f; break;
                case BodypartType.Torso: angle = 25f; break;
                case BodypartType.Head: angle = 20f; break;
                case BodypartType.Arm_L:
                case BodypartType.Arm_R: angle = -35f; break;
                default: return;
            }
            Vector3 forward = character.data.lookDirection_Flat.sqrMagnitude > 0.01f ? character.data.lookDirection_Flat.normalized : character.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Transform part = __instance.transform;
            part.rotation = Quaternion.AngleAxis(angle, right) * part.rotation;
        }
    }

    /// <summary>
    /// The game holds a scout up by their head's height, which the bend lowers, so it would push the whole body down
    /// and sink the legs into the ground. While bent over, the head is held where it is over the hip and the hip at its
    /// standing height instead.
    /// </summary>
    [HarmonyPatch(typeof(CharacterRagdoll), "SaveAdditionalTransformPositions")]
    private static class StandsOnTheGround
    {
        private static void Postfix(Character ___character)
        {
            Character character = ___character;
            if (!BentOver(character))
            {
                return;
            }
            Bodypart hip = character.GetBodypart(BodypartType.Hip);
            Bodypart head = character.GetBodypart(BodypartType.Head);
            if (hip == null || head == null || hip.Rig == null || head.Rig == null)
            {
                return;
            }
            float hipStanding = hip.transform.position.y - character.refs.rigCreator.transform.position.y;
            character.data.targetHeadHeight = hipStanding + (head.Rig.position.y - hip.Rig.position.y);
        }
    }

    private static SFX_Instance StomachSound()
    {
        if (stomach != null || stomachFailed)
        {
            return stomach;
        }
        try
        {
            AudioClip clip = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(NauseaResource), "Nausea_stomach");
            SFX_Instance sound = ScriptableObject.CreateInstance<SFX_Instance>();
            sound.name = "SFXI Nausea Stomach";
            sound.hideFlags = HideFlags.HideAndDontSave;
            sound.clips = new[] { clip };
            sound.settings = new SFX_Settings
            {
                volume = NauseaVolume,
                volume_Variation = 0f,
                pitch = 1f,
                pitch_Variation = 0.1f,
                spatialBlend = 1f,
                dopplerLevel = 0f,
                range = 40f,
                cooldown = 0.5f,
            };
            stomach = sound;
        }
        catch (Exception ex)
        {
            stomachFailed = true;
            Plugin.Log.LogError("The nausea's stomach sound could not be loaded: " + ex.Message);
        }
        return stomach;
    }
}
