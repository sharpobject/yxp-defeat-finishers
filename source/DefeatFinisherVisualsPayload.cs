using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Proto;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Networking;

// The native battle stage and real hit calculations remain active. This operation
// temporarily poses the winner and draws effects around the existing actors.
// Every equipped costume/color and the numbered clips/bones below are checked against
// their authored bytes by finisher_assets.py before installation.
internal static partial class DefeatFinisherVisualsPayload
{
    internal sealed class Playback
    {
        private readonly BattleExecuter executor;
        private readonly BattleCharacter winner, loser;
        private readonly int kind;
        private readonly BattleResult result;
        private readonly Movie movie = new Movie();
        private UniTask rendering;
        private bool started, closed, completed, finalRequested, finalDone, currentFinal;
        private float elapsed, lastHit, heldSince;
        public bool Closed { get { return closed; } }
        public bool Completed { get { return completed; } }
        public Playback(BattleExecuter executor, BattleCharacter winner, BattleCharacter loser, int kind)
        {
            this.executor = executor; this.winner = winner; this.loser = loser; this.kind = kind;
            result = executor.currentBattleResult;
        }
        public async UniTask Start()
        {
            try
            {
                if (!Alive(executor, result)) { closed = true; return; }
                movie.Create(executor, winner, loser, kind);
                await movie.LoadSound(kind, executor, result);
                if (!Alive(executor, result)) { Dispose(); return; }
                movie.sound.Play();
                started = true;
                rendering = Render();
            }
            catch (Exception exception)
            {
                Debug.LogError("Defeat finisher presentation failed: " + exception);
                Dispose();
            }
        }
        private async UniTask Render()
        {
            float previous = Time.realtimeSinceStartup;
            bool paused = false;
            try
            {
                while (!closed && (completed ? StillShown() : Alive(executor, result)))
                {
                    float now = Time.realtimeSinceStartup;
                    if (completed)
                    {
                        movie.DrawHeld(now - heldSince);
                        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                        continue;
                    }
                    float next = elapsed + now - previous;
                    previous = now;
                    float limit = kind == 1 ? (finalDone ? 6.8f : 2.75f) :
                        (!finalRequested ? (LinKnifeHit + 0.19f) : !finalDone ? (LinRollerHit + 0.16f) : LinDuration);
                    bool hold = next > limit && !(finalDone && limit == (kind == 1 ? 6.8f : LinDuration));
                    elapsed = Mathf.Min(next, limit);
                    if (hold && !paused) { movie.sound.Pause(); movie.sound.time = elapsed; paused = true; }
                    else if (!hold && paused) { movie.sound.UnPause(); paused = false; }
                    if (kind == 1) movie.Demon(elapsed); else movie.Roller(elapsed);
                    movie.UpdateImpact(now);
                    if (finalDone && elapsed >= (kind == 1 ? 6.8f : LinDuration))
                    {
                        movie.Hold(); heldSince = now; completed = true;
                    }
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Defeat finisher presentation failed: " + exception);
            }
            finally { closed = true; movie.Dispose(); }
        }
        private async UniTask WaitCue(float cue)
        {
            while (!closed && elapsed + 0.0001f < cue)
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        }
        public async UniTask BeforeCard(bool finalCard)
        {
            currentFinal = finalCard;
            if (finalCard) finalRequested = true;
            await WaitCue(kind == 1 ? 1.7f : finalCard ? LinRollerHit : LinKnifeHit);
        }
        public async UniTask BeforeHit(int index, int count)
        {
            float start = kind == 1 ? 1.7f : currentFinal ? LinRollerHit : LinKnifeHit;
            float span = kind == 1 ? 1.05f : currentFinal ? 0.16f : 0.19f;
            await WaitCue(start + span * index / Mathf.Max(1, count - 1));
            int beat = index * 15 / Mathf.Max(1, count);
            int previousBeat = (index - 1) * 15 / Mathf.Max(1, count);
            if (index == 0 || beat != previousBeat)
            {
                while (!closed && Time.realtimeSinceStartup < lastHit + 0.045f)
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                if (!closed)
                {
                    lastHit = Time.realtimeSinceStartup;
                    movie.Impact(currentFinal);
                }
            }
        }
        public void AfterCard(bool finalCard)
        {
            if (!finalCard) return;
            finalDone = true;
            if (!closed) movie.HideDefeatedStatusBar();
        }
        private bool StillShown()
        {
            return executor != null && executor.transform != null && executor.isVisible &&
                !executor.forceBreakExecuting && executor.currentBattleResult == result;
        }
        public async UniTask Finish()
        {
            while (started && !closed && !completed) await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        }
        public void Dispose() { closed = true; movie.Dispose(); }
    }

    private static bool Alive(BattleExecuter executor, BattleResult result)
    {
        return executor != null && executor.transform != null && executor.isVisible &&
            executor.isExecuting && !executor.forceBreakExecuting &&
            executor.currentBattleResult == result && BattleManager.Instance != null &&
            BattleManager.Instance.currentScene == SceneType.斗法阶段;
    }

    private sealed class Rig
    {
        internal CharacterBattleAnimator actor;
        internal Skeleton skeleton;
        internal Spine.Animation idle;
        internal Vector3 position;

        internal TuAuthoredPoses tuPose;
        internal Bone rightHand, leftHand;
        internal Slot rightHandSlot, leftHandSlot;
        internal Attachment openRightHand, openLeftHand, closedRightHand, closedLeftHand;
        internal Attachment curledRightHand, curledLeftHand, curledRightUnderHand, curledLeftUnderHand;
        internal Attachment[] profileParts, archParts;
        internal Spine.Animation[] linMotion;
        internal Spine.Animation sideView;
        internal Vector3 originalLocalPosition;
        internal bool originalEnabled;
        internal Color originalColor;
        internal float unit;
        internal void Borrow(BattleCharacter character, int kind)
        {
            actor = character.animator;
            if (actor == null || actor.skeletonAnimation == null)
                throw new InvalidOperationException("Finisher character unavailable.");
            skeleton = actor.skeletonAnimation.Skeleton;
            originalLocalPosition = actor.transform.localPosition;
            originalEnabled = actor.skeletonAnimation.enabled;
            originalColor = new Color(skeleton.R, skeleton.G, skeleton.B, skeleton.A);
            position = actor.transform.position;
            unit = Mathf.Abs(actor.skeletonAnimation.transform.lossyScale.y);
            idle = skeleton.Data.Animations.Items[kind == 1 ? 33 : actor.skinNumber == 2 ? 31 : 30];
            // Keep the native AnimationState and TrackEntry intact. Only this
            // operation applies poses; normal tracks resume when it ends.
            actor.skeletonAnimation.enabled = false;
        }
        internal void PrepareFinisher(int kind)
        {
            // Addressables and CharacterSkinConfig are checked together during
            // preparation. Recolors have their own binary and view clip order.
            if (kind == 1)
            {
                if (actor.skinNumber < 0 || actor.skinNumber > 1 || actor.skinColor < 0 || actor.skinColor > 1)
                    throw new InvalidOperationException("Unauthored Tu Kui finisher costume.");
                tuPose = new TuAuthoredPoses(this);
                return;
            }
            int skin = actor.skinNumber;
            if (skin == 0)
            {
                sideView = skeleton.Data.Animations.Items[actor.skinColor == 0 ? 45 : 41];
                rightHand = skeleton.Bones.Items[13]; leftHand = skeleton.Bones.Items[34];
                rightHandSlot = skeleton.Slots.Items[12]; leftHandSlot = skeleton.Slots.Items[13];
            }
            else if (skin == 1)
            {
                sideView = skeleton.Data.Animations.Items[42];
                rightHand = skeleton.Bones.Items[14]; leftHand = skeleton.Bones.Items[34];
                rightHandSlot = skeleton.Slots.Items[50]; leftHandSlot = skeleton.Slots.Items[5];
            }
            else if (skin == 2)
            {
                sideView = skeleton.Data.Animations.Items[42];
                rightHand = skeleton.Bones.Items[14]; leftHand = skeleton.Bones.Items[29];
                rightHandSlot = skeleton.Slots.Items[58]; leftHandSlot = skeleton.Slots.Items[48];
            }
            else throw new InvalidOperationException("Unauthored Lin Xiaoyue finisher costume.");
            Pose(19, 0f);
            closedRightHand = rightHandSlot.Attachment; closedLeftHand = leftHandSlot.Attachment;
            Pose(skin == 2 ? 20 : 21, skin == 2 ? 0.5f : 0.6f);
            openRightHand = rightHandSlot.Attachment; openLeftHand = leftHandSlot.Attachment;
            if (skin == 1) { Pose(38, 0f); openLeftHand = leftHandSlot.Attachment; }
            // Original native grip layers for Base/Snow; original curled paws for Hood.
            Pose(skin == 2 ? 5 : 1, 0.4f);
            curledRightHand = rightHandSlot.Attachment; curledLeftHand = leftHandSlot.Attachment;
            if (skin == 0)
            {
                curledRightUnderHand = skeleton.Slots.Items[3].Attachment;
                curledLeftUnderHand = skeleton.Slots.Items[4].Attachment;
            }
            else if (skin == 1)
            {
                curledRightUnderHand = skeleton.Slots.Items[28].Attachment;
                curledLeftUnderHand = skeleton.Slots.Items[4].Attachment;
            }
            PoseAnimation(sideView, 0.04f);
            archParts = LinAuthoredPoses.CaptureArch(skeleton, skin);
            // The native zero frame supplies the three-quarter head. Keep its
            // attachments paired with the same local transforms baked offline.
            PoseAnimation(sideView, 0f);
            profileParts = LinAuthoredPoses.CaptureProfile(skeleton, skin);
            linMotion = LinAuthoredPoses.Create(skin);
        }
        private void OpenHands()
        {
            rightHandSlot.Attachment = openRightHand;
            leftHandSlot.Attachment = openLeftHand;
            if (actor.skinNumber == 0)
            {
                skeleton.Slots.Items[3].Attachment = null;
                skeleton.Slots.Items[4].Attachment = null;
                skeleton.Slots.Items[11].Attachment = null;
            }
            else if (actor.skinNumber == 1)
            {
                skeleton.Slots.Items[4].Attachment = null;
                skeleton.Slots.Items[28].Attachment = null;
                skeleton.Slots.Items[29].Attachment = null;
                skeleton.Slots.Items[36].Attachment = null;
                skeleton.Slots.Items[43].Attachment = null;
            }
            else
            {
                for (int i = 41; i <= 47; i++) skeleton.Slots.Items[i].Attachment = null;
                for (int i = 51; i <= 57; i++) skeleton.Slots.Items[i].Attachment = null;
            }
        }
        private void LinClip(int index, float progress, bool open)
        {
            PoseAnimation(sideView, 0f);
            linMotion[0].Apply(skeleton, -1f, 0f, false, null, 1f, MixBlend.Replace, MixDirection.In);
            linMotion[index].Apply(skeleton, -1f, Mathf.Clamp01(progress), false, null, 1f, MixBlend.Replace, MixDirection.In);
            LinAuthoredPoses.ApplyProfile(skeleton, actor.skinNumber, profileParts);
            bool arch = index == 8 || (index == 7 && progress >= 0.5f);
            if (arch) LinAuthoredPoses.ApplyArch(skeleton, actor.skinNumber, archParts);
            OpenHands();
            if (!open) { rightHandSlot.Attachment = closedRightHand; leftHandSlot.Attachment = closedLeftHand; }
            if (index == 8 || (index == 7 && progress >= 0.4f))
            {
                rightHandSlot.Attachment = curledRightHand;
                leftHandSlot.Attachment = curledLeftHand;
                if (actor.skinNumber == 0)
                {
                    skeleton.Slots.Items[3].Attachment = curledRightUnderHand;
                    skeleton.Slots.Items[4].Attachment = curledLeftUnderHand;
                }
                else if (actor.skinNumber == 1)
                {
                    skeleton.Slots.Items[28].Attachment = curledRightUnderHand;
                    skeleton.Slots.Items[4].Attachment = curledLeftUnderHand;
                }
            }
            LinAuthoredPoses.ApplyOrder(skeleton, actor.skinNumber, actor.skinColor, arch);
        }
        internal void PoseStop(float time)
        {
            if (time < 0.3f) LinClip(1, time / 0.3f, false);
            else if (time < LinKnifeReadyOne - 0.22f)
                LinClip(9, (time - 0.3f) / (LinKnifeReadyOne - 0.52f), false);
            else LinClip(2, (time - LinKnifeReadyOne + 0.22f) / 0.22f, false);
        }
        internal void PoseKnives(float phase, bool second)
        {
            if (phase < 0.56f || !second)
                LinClip(3, phase / 0.56f, phase >= LinFirstKnives - LinKnifeReadyOne);
            else
            {
                float pulse = 2f * (phase - 0.56f) / (LinJumpStart - LinKnifeReadyTwo - 0.56f);
                LinClip(10, pulse - Mathf.Floor(pulse), true);
            }
        }
        internal void PoseJump(float progress) { LinClip(4, progress, false); }
        internal void PoseRoller(float impactTime)
        {
            if (impactTime < 0f)
                LinClip(5, (impactTime + LinRollerHit - LinDropStart) / (LinRollerHit - LinDropStart), false);
            else LinClip(6, impactTime / (LinTriumphStart - LinRollerHit - 0.16f), false);
        }
        internal void PoseTriumph(float progress) { LinClip(7, progress, progress >= 0.4f); }
        internal void PoseBreathing(float phase) { LinClip(8, phase, true); }
        internal void Pose(int clip, float time)
        {
            PoseAnimation(skeleton.Data.Animations.Items[clip], time);
        }
        internal void PoseAnimation(Spine.Animation animation, float time)
        {
            skeleton.SetToSetupPose();
            animation.Apply(skeleton, -1f, Mathf.Clamp(time, 0f, animation.Duration), false,
                null, 1f, MixBlend.Replace, MixDirection.In);
            skeleton.R = skeleton.G = skeleton.B = skeleton.A = 1f;
            ApplyPosition();
        }
        internal void ApplyPosition()
        {
            actor.transform.position = position;
        }
        internal void PoseDemon(int frame) { tuPose.Apply(frame); }
        internal void PoseBack() { tuPose.Apply(3); }
        internal void Draw()
        {
            skeleton.UpdateWorldTransform(Skeleton.Physics.Pose);
            actor.skeletonAnimation.LateUpdate();
        }
        internal void Dispose()
        {
            if (actor == null) return;
            if (actor.transform == null || actor.skeletonAnimation == null) { actor = null; return; }
            actor.transform.localPosition = originalLocalPosition;
            skeleton.SetToSetupPose();
            actor.skeletonAnimation.AnimationState.Apply(skeleton);
            skeleton.R = originalColor.r; skeleton.G = originalColor.g;
            skeleton.B = originalColor.b; skeleton.A = originalColor.a;
            Draw();
            actor.skeletonAnimation.enabled = originalEnabled;
            actor = null;
        }
    }

    private sealed class MeshTrail
    {
        private readonly MeshFilter source;
        private readonly Transform sourceTransform;
        private readonly GameObject[] objects = new GameObject[4];
        private readonly MeshFilter[] filters = new MeshFilter[4];
        private readonly MeshRenderer[] renderers = new MeshRenderer[4];
        private readonly Mesh[] meshes = new Mesh[4];
        private readonly Color32[][] originals = new Color32[4][];
        private readonly Color32[][] faded = new Color32[4][];
        private readonly float[] born = new float[4];
        private int next;
        private float nextCapture;
        internal MeshTrail(Rig rig, Transform parent)
        {
            source = rig.actor.skeletonAnimation.GetComponent<MeshFilter>();
            sourceTransform = source.transform;
            MeshRenderer native = source.GetComponent<MeshRenderer>();
            Material[] materials = native.sharedMaterials;
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject item = new GameObject("NativePoseAfterimage");
                item.layer = source.gameObject.layer;
                item.transform.SetParent(parent, false);
                objects[i] = item;
                filters[i] = item.AddComponent<MeshFilter>();
                renderers[i] = item.AddComponent<MeshRenderer>();
                renderers[i].sharedMaterials = materials;
                renderers[i].sortingLayerID = native.sortingLayerID;
                renderers[i].sortingOrder = native.sortingOrder - 1;
                renderers[i].enabled = false;
            }
        }
        internal void Update(float now, bool emitting)
        {
            if (emitting && now >= nextCapture && source != null && source.sharedMesh != null)
            {
                int index = next; next = (next + 1) % objects.Length;
                nextCapture = now + 0.06f;
                if (meshes[index] != null) UnityEngine.Object.Destroy(meshes[index]);
                meshes[index] = UnityEngine.Object.Instantiate<Mesh>(source.sharedMesh);
                originals[index] = meshes[index].colors32;
                faded[index] = new Color32[originals[index].Length];
                filters[index].sharedMesh = meshes[index];
                Transform transform = objects[index].transform;
                transform.position = sourceTransform.position;
                transform.rotation = sourceTransform.rotation;
                transform.localScale = sourceTransform.lossyScale;
                born[index] = now;
            }
            for (int i = 0; i < objects.Length; i++)
            {
                if (meshes[i] == null) continue;
                float alpha = Mathf.Clamp01(1f - (now - born[i]) / 0.22f) * 0.55f;
                renderers[i].enabled = alpha > 0f;
                if (alpha <= 0f) continue;
                for (int vertex = 0; vertex < faded[i].Length; vertex++)
                {
                    Color32 original = originals[i][vertex];
                    // The native Spine material uses premultiplied vertex color.
                    faded[i][vertex] = new Color32((byte)(original.r * alpha), (byte)(original.g * alpha),
                        (byte)(original.b * alpha), (byte)(original.a * alpha));
                }
                meshes[i].colors32 = faded[i];
            }
        }
        internal void Hide()
        {
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
        }
        internal void Dispose()
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (meshes[i] != null) UnityEngine.Object.Destroy(meshes[i]);
                if (objects[i] != null) UnityEngine.Object.Destroy(objects[i]);
            }
        }
    }

    private sealed class Movie
    {
        internal BattleExecuter executor;
        internal CharacterBattleAnimator target;
        internal Rig attacker = new Rig();
        internal GameObject effects;
        internal Camera camera;
        internal MeshRenderer actorRenderer, targetRenderer;
        internal bool actorRendererEnabled, targetRendererEnabled, demonDark;
        internal GameObject defeatedStatusBar;
        internal bool defeatedStatusBarWasActive;
        internal MeshTrail trail;
        internal SpriteRenderer wash;
        internal Sprite washSprite;
        internal float flameScale;
        internal AudioSource sound;
        internal AudioClip clip;
        internal Texture2D rollerTexture, duckTexture, flameTexture, knifeTexture, superTexture, portraitTexture;
        internal Sprite rollerSprite, duckSprite, knifeSprite, superSprite, portraitSprite;
        internal Sprite[] flameSprites;
        internal SpriteRenderer roller, duck, backDuck, flame, superFlash, portrait;
        internal SpriteRenderer[] knives;
        internal Vector3[] knifeOrigins;
        internal bool[] knifeEmitted;
        internal Vector3 start, victim, contact;
        internal float direction, targetTimeScale, rollerWidth, rollerHeight, impactAt = -10f;
        internal int kind, sortingLayer, sortingOrder;
        internal bool disposed, targetFrozen, rollerShook;

        internal Texture2D tuActivationTexture, tuHitsTexture;
        internal Sprite[] tuActivationSprites, tuHitSprites;
        internal SpriteRenderer tuActivation;
        internal SpriteRenderer[] tuBursts;
        internal float[] tuBurstStarted;
        internal int[] tuBurstFirstFrame;
        internal int nextTuBurst;

        private void CreateTuArcadeEffects()
        {
            tuActivationTexture = Texture("tu-third-strike-superflash.png", true);
            tuActivationSprites = new Sprite[44];
            for (int i = 0; i < tuActivationSprites.Length; i++)
                tuActivationSprites[i] = Sprite.Create(tuActivationTexture,
                    new Rect((i % 8) * 432f, tuActivationTexture.height - (i / 8 + 1) * 336f, 432f, 336f),
                    new Vector2(0.5f, 0.5f), 100f);
            tuActivation = Picture("ThirdStrikeActivation", tuActivationSprites[0], sortingOrder + 2);
            tuActivation.transform.localScale = Vector3.one * (10f * attacker.unit * 100f / 336f);
            tuActivation.gameObject.SetActive(false);

            tuHitsTexture = Texture("tu-third-strike-hit-sparks.png", true);
            tuHitSprites = new Sprite[30];
            for (int i = 0; i < tuHitSprites.Length; i++)
                tuHitSprites[i] = Sprite.Create(tuHitsTexture,
                    new Rect((i % 10) * 160f, tuHitsTexture.height - (i / 10 + 1) * 160f, 160f, 160f),
                    new Vector2(0.5f, 0.5f), 100f);
            // A burst lasts 1/6 s and actual hits can arrive every .045 s. Four rendered
            // bursts let those original frames finish while subsequent hits begin.
            tuBursts = new SpriteRenderer[4];
            tuBurstStarted = new float[4];
            tuBurstFirstFrame = new int[4];
            for (int i = 0; i < tuBursts.Length; i++)
            {
                tuBursts[i] = Picture("ThirdStrikeHit" + i, tuHitSprites[0], sortingOrder + 3);
                tuBursts[i].transform.localScale = Vector3.one * (6f * attacker.unit * 100f / 160f);
                tuBursts[i].gameObject.SetActive(false);
            }
        }

        private void DrawTuActivation(float time)
        {
            bool visible = time >= 0f && time < 0.75f;
            tuActivation.gameObject.SetActive(visible);
            if (!visible) return;
            int frame = time < 0.55f
                ? Mathf.Min(31, (int)(time * (32f / 0.55f)))
                : 32 + Mathf.Min(11, (int)((time - 0.55f) * 60f));
            tuActivation.sprite = tuActivationSprites[frame];
            tuActivation.transform.position = attacker.actor.hitPoint.position - camera.transform.forward * 0.02f;
        }

        private void RecordTuImpact(bool finalCard)
        {
            int index = nextTuBurst;
            nextTuBurst = (index + 1) % tuBursts.Length;
            tuBurstStarted[index] = Time.realtimeSinceStartup;
            // Three original ten-frame burst sizes are retained in the atlas. Use a
            // medium spark for prior-card hits and the large spark for Realm palms.
            tuBurstFirstFrame[index] = finalCard ? 20 : 10;
            SpriteRenderer burst = tuBursts[index];
            burst.sprite = tuHitSprites[tuBurstFirstFrame[index]];
            float x = index % 2 == 0 ? -0.35f : 0.35f;
            float y = index < 2 ? 0.35f : -0.2f;
            burst.transform.position = target.hitPoint.position +
                new Vector3(x * attacker.unit, y * attacker.unit, 0f) - camera.transform.forward * 0.03f;
            burst.gameObject.SetActive(true);
        }

        private void UpdateTuImpacts(float now)
        {
            for (int i = 0; i < tuBursts.Length; i++)
            {
                if (!tuBursts[i].gameObject.activeSelf) continue;
                int frame = (int)((now - tuBurstStarted[i]) * 60f);
                if (frame >= 10) tuBursts[i].gameObject.SetActive(false);
                else tuBursts[i].sprite = tuHitSprites[tuBurstFirstFrame[i] + Mathf.Max(0, frame)];
            }
        }

        private void DisposeTuArcadeEffects()
        {
            // Renderers are children of Movie.effects and are destroyed with it.
            if (tuActivationSprites != null)
                for (int i = 0; i < tuActivationSprites.Length; i++)
                    if (tuActivationSprites[i] != null) UnityEngine.Object.Destroy(tuActivationSprites[i]);
            if (tuHitSprites != null)
                for (int i = 0; i < tuHitSprites.Length; i++)
                    if (tuHitSprites[i] != null) UnityEngine.Object.Destroy(tuHitSprites[i]);
            if (tuActivationTexture != null) UnityEngine.Object.Destroy(tuActivationTexture);
            if (tuHitsTexture != null) UnityEngine.Object.Destroy(tuHitsTexture);
        }

        internal void Create(BattleExecuter battle, BattleCharacter winner, BattleCharacter loser, int movieKind)
        {
            executor = battle; kind = movieKind;
            camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Battle camera unavailable.");
            target = loser.animator;
            if (target == null) throw new InvalidOperationException("Finisher opponent unavailable.");
            defeatedStatusBar = DefeatFinisherPayload.ReadStatusBar(loser.characterUI).gameObject;
            defeatedStatusBarWasActive = defeatedStatusBar.activeSelf;
            attacker.Borrow(winner, kind);
            attacker.PrepareFinisher(kind);
            start = attacker.position;
            victim = target.transform.position;
            direction = winner == executor.rightCharacter ? -1f : 1f;
            contact = victim - Vector3.right * (direction * attacker.unit * 1.2f);
            actorRenderer = attacker.actor.skeletonAnimation.GetComponent<MeshRenderer>();
            targetRenderer = target.skeletonAnimation.GetComponent<MeshRenderer>();
            actorRendererEnabled = actorRenderer.enabled; targetRendererEnabled = targetRenderer.enabled;
            sortingLayer = actorRenderer.sortingLayerID; sortingOrder = actorRenderer.sortingOrder;
            effects = new GameObject("__FinisherEffects");
            effects.layer = attacker.actor.skeletonAnimation.gameObject.layer;
            trail = new MeshTrail(attacker, effects.transform);
            washSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), 100f);
            // The world flash stays below actors and native damage text. Its
            // requested 500-pixel margin on each edge covers native camera shake.
            wash = Picture("BattleFlash", washSprite, sortingOrder - 2);
            wash.color = Color.clear;
            float plane = Vector3.Dot(start - camera.transform.position, camera.transform.forward) + 0.1f;
            wash.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, plane));
            Vector3 low = camera.ViewportToWorldPoint(new Vector3(0f, 0f, plane));
            Vector3 high = camera.ViewportToWorldPoint(new Vector3(1f, 1f, plane));
            float washWidth = (high.x - low.x) * (1f + 1000f / Mathf.Max(1, camera.pixelWidth));
            float washHeight = (high.y - low.y) * (1f + 1000f / Mathf.Max(1, camera.pixelHeight));
            wash.transform.localScale = new Vector3(washWidth * 100f / Texture2D.whiteTexture.width,
                washHeight * 100f / Texture2D.whiteTexture.height, 1f);
            sound = effects.AddComponent<AudioSource>();
            sound.playOnAwake = false; sound.spatialBlend = 0f;
            sound.volume = Mathf.Clamp01(SettingsManager.settings.seVolume / 100f);
            portraitTexture = Texture(kind == 1 ? "tu-portrait-strip.png" : "lin-portrait-strip.png", false);
            int portraitRows = kind == 1 ? 2 : 3;
            portraitSprite = Sprite.Create(portraitTexture,
                new Rect(0f, (portraitRows - 1 - attacker.actor.skinNumber) * 256f, 768f, 256f),
                new Vector2(0.5f, 0.5f), 100f);
            portrait = Picture("FinisherPortrait", portraitSprite, sortingOrder + 4);
            // Every authored cell faces right; the live actor's side determines
            // the presentation, independently of costume or native art facing.
            portrait.flipX = direction < 0f;
            portrait.gameObject.SetActive(false);
            if (kind == 1)
            {
                // The grip must stay visible over the opponent at contact.
                // Keep stage effects at their existing depth and restore the
                // borrowed renderer's order when this battle leaves the screen.
                actorRenderer.sortingOrder = Mathf.Max(sortingOrder, targetRenderer.sortingOrder) + 1;
                CreateTuArcadeEffects();
                duckTexture = Texture("duck-calligraphy.png", false);
                duckSprite = Sprite.Create(duckTexture, new Rect(0f, 0f, duckTexture.width, duckTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                duck = Picture("BackgroundDuck", duckSprite, sortingOrder - 1);
                backDuck = Picture("BackDuck", duckSprite, actorRenderer.sortingOrder + 1);
                duck.gameObject.SetActive(false); backDuck.gameObject.SetActive(false);
                flameTexture = Texture("tu-third-strike-flame.png", true);
                flameSprites = new Sprite[30];
                for (int i = 0; i < flameSprites.Length; i++)
                    flameSprites[i] = Sprite.Create(flameTexture,
                        new Rect((i % 6) * 290f, flameTexture.height - (i / 6 + 1) * 265f, 290f, 265f),
                        new Vector2(145f / 290f, 1f - 212f / 265f), 100f);
                flame = Picture("ThirdStrikeFlame", flameSprites[0], sortingOrder - 1);
                flame.gameObject.SetActive(false);
            }
            else
            {
                targetTimeScale = target.skeletonAnimation.timeScale;
                target.skeletonAnimation.timeScale = 0f;
                targetFrozen = true;
                rollerTexture = Texture("steamroller.png", true);
                rollerSprite = Sprite.Create(rollerTexture, new Rect(0f, 0f, rollerTexture.width, rollerTexture.height),
                    new Vector2(0.5f, 0f), 100f);
                roller = Picture("ArcadeRoadRoller", rollerSprite, sortingOrder + 1);
                roller.flipX = direction < 0f;
                roller.gameObject.SetActive(false);
                superTexture = Texture("dio-superflash.png", false);
                superSprite = Sprite.Create(superTexture, new Rect(0f, 0f, superTexture.width, superTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                superFlash = Picture("ArcadeSuperFlash", superSprite, sortingOrder + 5);
                superFlash.gameObject.SetActive(false);
                knifeTexture = Texture("dio-knife.png", true);
                knifeSprite = Sprite.Create(knifeTexture, new Rect(0f, 0f, knifeTexture.width, knifeTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                knives = new SpriteRenderer[24]; knifeOrigins = new Vector3[24]; knifeEmitted = new bool[24];
                for (int i = 0; i < knives.Length; i++)
                {
                    knives[i] = Picture("ArcadeKnife", knifeSprite, sortingOrder + 2);
                    knives[i].transform.localScale = Vector3.one * (1.6f * attacker.unit * 100f / knifeTexture.width);
                    knives[i].gameObject.SetActive(false);
                }
            }
        }
        private Texture2D Texture(string name, bool pixelArt)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,
                "YxpFinishers", name)), true))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("Finisher artwork unavailable: " + name);
            }
            texture.filterMode = pixelArt ? FilterMode.Point : FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }
        private SpriteRenderer Picture(string name, Sprite sprite, int order)
        {
            GameObject item = new GameObject(name);
            item.layer = effects.layer;
            item.transform.SetParent(effects.transform, false);
            item.transform.rotation = camera.transform.rotation;
            SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerID = sortingLayer; renderer.sortingOrder = order;
            return renderer;
        }
        internal async UniTask LoadSound(int movieKind, BattleExecuter battle, BattleResult result)
        {
            string file = Path.Combine(Application.streamingAssetsPath, "YxpFinishers",
                movieKind == 1 ? "tu-raging-demon.wav" : "lin-roadroller.wav");
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(file).AbsoluteUri, AudioType.WAV))
            {
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (!Alive(battle, result)) { request.Abort(); return; }
                    await UniTask.Yield();
                }
                if (request.result != UnityWebRequest.Result.Success) throw new IOException("Finisher audio: " + request.error);
                clip = DownloadHandlerAudioClip.GetContent(request);
                sound.clip = clip;
            }
        }
        internal void Demon(float time)
        {
            float dim = time < 0.75f ? 0.6f * Mathf.Clamp01(time / 0.1f) :
                time < 1.7f ? 0.35f : time < 2.8f ? 1f :
                time < 4.8f ? 0.35f : 0.35f * (1f - Mathf.Clamp01((time - 4.8f) / 0.4f));
            wash.color = new Color(0f, 0f, 0f, dim);
            attacker.position = start;
            if (time < 0.75f)
            {
                attacker.PoseDemon(4);
                if (time > 0.12f && time < 0.21f) wash.color = new Color(1f, 1f, 1f, 0.85f);
            }
            else if (time < 1.7f)
            {
                float glide = Mathf.Clamp01((time - 0.75f) / 0.6f);
                attacker.position = Vector3.Lerp(start, contact, glide);
                attacker.PoseDemon(time < 1.35f ? 0 : time < 1.39f ? 5 : 6);
                if (time < 1.39f)
                { attacker.skeleton.R = 0.65f; attacker.skeleton.G = 0.28f; }
            }
            else if (time < 2.8f)
            {
                attacker.position = contact;
                attacker.PoseDemon(6);
            }
            else
            {
                attacker.position = contact;
                // The turn happens during the blackout. Third Strike reveals
                // the planted rear stance, never the airborne approach pose.
                attacker.PoseDemon(3);
                flame.gameObject.SetActive(time < 4.8f);
                if (time < 4.8f)
                {
                    flame.sprite = flameSprites[Mathf.Min(29, (int)((time - 2.8f) * 15f))];
                    flame.transform.position = contact + camera.transform.forward * 0.05f;
                }
                else
                {
                    duck.gameObject.SetActive(true); backDuck.gameObject.SetActive(true);
                    float plane = Vector3.Dot(contact - camera.transform.position, camera.transform.forward);
                    duck.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, plane + 0.1f));
                    float height = camera.orthographic ? camera.orthographicSize * 1.7f : attacker.unit * 12f;
                    duck.transform.localScale = Vector3.one * (height * 100f / duckTexture.height);
                    duck.color = new Color(1f, 1f, 1f, Mathf.Clamp01((time - 4.8f) * 8f) * 0.8f);
                }
            }
            attacker.Draw();
            trail.Update(Time.realtimeSinceStartup, time >= 0.75f && time < 1.35f);
            SetDemonDark(time >= 1.7f && time < 2.8f);
            if (demonDark) trail.Hide();
            DrawTuActivation(time);
            Portrait(time);
            if (flame.gameObject.activeSelf)
            {
                if (flameScale <= 0f) flameScale = actorRenderer.bounds.size.y / 1.19f;
                flame.transform.localScale = Vector3.one * flameScale;
            }
            if (backDuck.gameObject.activeSelf)
            {
                Bone chest = attacker.skeleton.Bones.Items[3];
                backDuck.transform.position = attacker.actor.skeletonAnimation.transform.TransformPoint(
                    new Vector3(chest.WorldX, chest.WorldY, 0f)) + camera.transform.up * (0.35f * attacker.unit) - camera.transform.forward * 0.02f;
                backDuck.transform.localScale = Vector3.one * (1.1f * attacker.unit * 100f / duckTexture.width);
            }
        }
        private void SetDemonDark(bool dark)
        {
            if (dark)
            {
                actorRenderer.enabled = false; targetRenderer.enabled = false;
                demonDark = true;
            }
            else if (demonDark)
            {
                actorRenderer.enabled = actorRendererEnabled; targetRenderer.enabled = targetRendererEnabled;
                demonDark = false;
            }
        }
        internal void Roller(float time)
        {
            float dim = time < LinKnifeHit ? 0.55f * Mathf.Clamp01(time / 0.16f) :
                time < LinJumpStart ? 0.16f : time < LinRollerHit ? 0.6f :
                0.3f * (1f - Mathf.Clamp01((time - LinRollerHit) / 0.3f));
            wash.color = new Color(0f, 0f, 0f, dim);
            attacker.position = start;
            float knifePhase = time < LinKnifeReadyTwo ? time - LinKnifeReadyOne : time - LinKnifeReadyTwo;
            if (targetFrozen && time >= LinKnifeHit) { target.skeletonAnimation.timeScale = targetTimeScale; targetFrozen = false; }
            if (time < LinKnifeReadyOne)
            {
                attacker.PoseStop(time);
                if (time > 0.32f && time < 0.42f) wash.color = new Color(1f, 1f, 1f, 0.9f);
            }
            else if (time < LinJumpStart)
            {
                attacker.PoseKnives(time < LinKnifeReadyTwo ? time - LinKnifeReadyOne : time - LinKnifeReadyTwo,
                    time >= LinKnifeReadyTwo);
            }
            else if (time < LinDropStart)
            {
                float jump = Mathf.Clamp01((time - LinJumpStart) / 0.2f);
                float height = camera.orthographic ? camera.orthographicSize * 2.4f : attacker.unit * 14f;
                // Compress on the stage first, then rise vertically. The rider
                // reappears over the target only after leaving the camera view.
                float launch = Mathf.Clamp01((jump - 0.25f) / 0.75f);
                attacker.position = start + Vector3.up * (launch * launch * height);
                attacker.PoseJump(jump);
            }
            else if (time < LinTriumphStart - 0.16f)
            {
                roller.gameObject.SetActive(true);
                float fall = Mathf.Clamp01((time - LinDropStart) / 0.2f);
                float above = camera.orthographic ? camera.orthographicSize * 2.4f : attacker.unit * 14f;
                float drop = (1f - fall * fall) * above;
                roller.transform.position = victim + Vector3.up * drop;
                attacker.position = RollerRiderPosition();
                attacker.PoseRoller(time - LinRollerHit);
                if (fall >= 1f && !rollerShook)
                {
                    executor.ShakeScreen(0.45f, 0.25f, 25f, 0.2f, null);
                    rollerShook = true;
                }
            }
            else
            {
                roller.gameObject.SetActive(true);
                roller.transform.position = victim;
                attacker.position = RollerRiderPosition();
                if (time < LinTriumphStart)
                    attacker.PoseTriumph((time - LinTriumphStart + 0.16f) / 0.16f);
                else attacker.PoseBreathing(((time - LinTriumphStart) % 0.9f) / 0.9f);
            }
            attacker.Draw();
            trail.Update(Time.realtimeSinceStartup, knifePhase >= 0.23f && knifePhase < 0.58f);
            if (rollerWidth <= 0f)
            {
                // The first posed frame has no sword and supplies this costume's
                // actual standing height. All rider anchors use the same scale.
                rollerWidth = actorRenderer.bounds.size.y * 2.2f;
                rollerHeight = rollerWidth * rollerTexture.height / rollerTexture.width;
                roller.transform.localScale = Vector3.one * (rollerWidth * 100f / rollerTexture.width);
            }
            float flashStart = time >= LinJumpStart + 0.05f ? LinJumpStart + 0.05f :
                time >= LinKnifeReadyTwo + 0.05f ? LinKnifeReadyTwo + 0.05f :
                time >= LinKnifeReadyOne + 0.05f ? LinKnifeReadyOne + 0.05f : 0.32f;
            Portrait(time);
            SuperFlash(time - flashStart, time >= LinJumpStart ? target.hitPoint.position : attacker.actor.hitPoint.position);
            if (time >= LinKnifeReadyOne && time < LinKnifeHit + 0.25f) DrawKnives(time);
            else if (knives != null) for (int i = 0; i < knives.Length; i++) knives[i].gameObject.SetActive(false);
        }
        private void Portrait(float time)
        {
            bool visible = time >= 0.05f && time < 0.55f;
            portrait.gameObject.SetActive(visible);
            if (!visible) return;
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - 0.05f) / 0.11f));
            float leave = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - 0.4f) / 0.15f));
            float plane = Vector3.Dot(start - camera.transform.position, camera.transform.forward) - 0.04f;
            float x = 0.5f + direction * ((1f - enter) - leave) * 1.1f;
            portrait.transform.position = camera.ViewportToWorldPoint(new Vector3(x, 0.68f, plane));
            float width = Vector3.Distance(camera.ViewportToWorldPoint(new Vector3(0f, 0.5f, plane)),
                camera.ViewportToWorldPoint(new Vector3(1f, 0.5f, plane)));
            portrait.transform.localScale = Vector3.one * (width * 100f / 768f);
            portrait.color = new Color(1f, 1f, 1f, Mathf.Clamp01(enter * 2f) * (1f - leave));
        }
        private void SuperFlash(float phase, Vector3 center)
        {
            bool active = phase >= 0f && phase < 0.24f;
            superFlash.gameObject.SetActive(active);
            if (!active) return;
            float p = phase / 0.24f;
            float height = camera.orthographic ? camera.orthographicSize * 2f : attacker.unit * 12f;
            float scale = height * 100f / superTexture.height;
            superFlash.transform.position = center - camera.transform.forward * 0.03f;
            superFlash.transform.localScale = new Vector3(
                (0.08f + Mathf.Sin(p * Mathf.PI) * 1.65f) * scale,
                (0.08f + Mathf.Sin(Mathf.Min(1f, p * 1.3f) * Mathf.PI) * 1.35f) * scale, 1f);
            superFlash.color = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - p) * 2.5f));
        }
        private Vector3 RollerRiderPosition()
        {
            // Native shoe soles sit at different heights relative to each rig's
            // root. Place the rider on the roof without changing leg geometry.
            float soleOffset = attacker.actor.skinNumber == 0 ? -0.13f :
                attacker.actor.skinNumber == 1 ? -0.16f : 0.08f;
            return roller.transform.position + Vector3.up * (rollerHeight * (1f - 13f / 160f) + soleOffset * attacker.unit) +
                Vector3.right * (direction * (153f / 331f - 0.5f) * rollerWidth);
        }
        private Vector3 HandPoint(bool right)
        {
            Bone hand = right ? attacker.rightHand : attacker.leftHand;
            return attacker.actor.skeletonAnimation.transform.TransformPoint(new Vector3(hand.WorldX, hand.WorldY, 0f));
        }
        private void DrawKnives(float time)
        {
            Vector3 hit = target.hitPoint.position;
            for (int i = 0; i < knives.Length; i++)
            {
                int fan = i % 6;
                float ready = i < 12 ? LinKnifeReadyOne : LinKnifeReadyTwo;
                float began = (i < 12 ? LinFirstKnives : LinSecondKnives) + fan * 0.018f;
                if (time < ready) continue;
                Vector3 hand = HandPoint(i % 12 < 6);
                float angle = 35f + fan * 19f;
                if (i % 12 < 6) angle = 180f - angle;
                if (direction < 0f) angle = 180f - angle;
                Vector3 fanDirection = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f);
                knives[i].gameObject.SetActive(true);
                if (time < began)
                {
                    knives[i].transform.position = hand + fanDirection * (0.5f * attacker.unit);
                    knives[i].transform.rotation = Quaternion.Euler(0f, 0f, angle);
                    continue;
                }
                if (!knifeEmitted[i]) { knifeOrigins[i] = hand; knifeEmitted[i] = true; }
                float travel = Mathf.Clamp01((time - began) / 0.18f);
                float release = Mathf.Clamp01((time - LinKnifeHit - (i % 12) * 0.008f) / 0.1f);
                Vector3 held = hit - Vector3.right * (direction * attacker.unit * (1.7f + i % 4 * 0.4f)) +
                    Vector3.up * ((i % 12 - 5.5f) * attacker.unit * 0.25f);
                Vector3 destination = hit + Vector3.up * ((i % 5 - 2) * attacker.unit * 0.2f);
                knives[i].gameObject.SetActive(release < 1f);
                knives[i].transform.position = Vector3.Lerp(Vector3.Lerp(knifeOrigins[i], held, travel), destination, release);
                Vector3 heading = release > 0f ? destination - held : held - knifeOrigins[i];
                knives[i].transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg);
            }
        }
        internal void Impact(bool finalCard)
        {
            impactAt = Time.realtimeSinceStartup;
            if (kind == 1) RecordTuImpact(finalCard);
            executor.ShakeScreen(kind == 2 && finalCard ? 0.45f : 0.08f, 0.06f, 25f, 0.08f, null);
        }
        internal void UpdateImpact(float now)
        {
            if (kind == 1) UpdateTuImpacts(now);
            float age = now - impactAt;
            if (!demonDark && age >= 0f && age < 0.03f) wash.color = new Color(1f, 0.97f, 0.9f, (1f - age / 0.03f) * 0.85f);
        }
        internal void DrawHeld(float elapsed)
        {
            if (kind == 1)
            {
                attacker.PoseBack();
                float breath = (1f - Mathf.Cos(elapsed * (Mathf.PI * 2f / 2.8f))) * 0.5f;
                attacker.skeleton.Bones.Items[3].X += 0.018f * breath;
                attacker.skeleton.Bones.Items[3].Rotation += 0.35f * breath;
            }
            else attacker.PoseBreathing(((LinDuration - LinTriumphStart + elapsed) % 0.9f) / 0.9f);
            attacker.Draw();
            if (kind == 1 && backDuck.gameObject.activeSelf)
            {
                Bone chest = attacker.skeleton.Bones.Items[3];
                backDuck.transform.position = attacker.actor.skeletonAnimation.transform.TransformPoint(
                    new Vector3(chest.WorldX, chest.WorldY, 0f)) + camera.transform.up * (0.35f * attacker.unit) - camera.transform.forward * 0.02f;
            }
        }
        internal void Hold()
        {
            if (sound != null) sound.Stop();
            wash.color = Color.clear;
            if (trail != null) { trail.Dispose(); trail = null; }
            if (tuActivation != null) tuActivation.gameObject.SetActive(false);
            if (tuBursts != null) for (int i = 0; i < tuBursts.Length; i++) tuBursts[i].gameObject.SetActive(false);
            if (superFlash != null) superFlash.gameObject.SetActive(false);
            if (portrait != null) portrait.gameObject.SetActive(false);
            if (flame != null) flame.gameObject.SetActive(false);
            if (knives != null) for (int i = 0; i < knives.Length; i++) knives[i].gameObject.SetActive(false);
        }
        internal void HideDefeatedStatusBar()
        {
            // AfterCard(true) runs only after native lethal damage and any
            // resurrection have settled. Floating text belongs to a different
            // parent; cards, avatars and deck buffs also remain untouched.
            if (defeatedStatusBar != null) defeatedStatusBar.SetActive(false);
        }
        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (defeatedStatusBar != null) defeatedStatusBar.SetActive(defeatedStatusBarWasActive);
            if (demonDark)
            {
                if (actorRenderer != null) actorRenderer.enabled = actorRendererEnabled;
                if (targetRenderer != null) targetRenderer.enabled = targetRendererEnabled;
                demonDark = false;
            }
            if (actorRenderer != null) actorRenderer.sortingOrder = sortingOrder;
            if (sound != null) sound.Stop();
            if (targetFrozen && target != null && target.skeletonAnimation != null)
                target.skeletonAnimation.timeScale = targetTimeScale;
            if (trail != null) trail.Dispose();
            DisposeTuArcadeEffects();
            attacker.Dispose();
            if (effects != null) UnityEngine.Object.Destroy(effects);
            if (clip != null) UnityEngine.Object.Destroy(clip);
            if (washSprite != null) UnityEngine.Object.Destroy(washSprite);
            if (rollerSprite != null) UnityEngine.Object.Destroy(rollerSprite);
            if (duckSprite != null) UnityEngine.Object.Destroy(duckSprite);
            if (knifeSprite != null) UnityEngine.Object.Destroy(knifeSprite);
            if (superSprite != null) UnityEngine.Object.Destroy(superSprite);
            if (portraitSprite != null) UnityEngine.Object.Destroy(portraitSprite);
            if (flameSprites != null) for (int i = 0; i < flameSprites.Length; i++)
                if (flameSprites[i] != null) UnityEngine.Object.Destroy(flameSprites[i]);
            if (rollerTexture != null) UnityEngine.Object.Destroy(rollerTexture);
            if (duckTexture != null) UnityEngine.Object.Destroy(duckTexture);
            if (flameTexture != null) UnityEngine.Object.Destroy(flameTexture);
            if (knifeTexture != null) UnityEngine.Object.Destroy(knifeTexture);
            if (superTexture != null) UnityEngine.Object.Destroy(superTexture);
            if (portraitTexture != null) UnityEngine.Object.Destroy(portraitTexture);
        }
    }

}
