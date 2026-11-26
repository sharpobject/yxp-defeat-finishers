using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Proto;
using TrackEntry = Spine.TrackEntry;
using UnityEngine;
using Yx.ModSdk;
using Yx.ModSdk.Hooks;
using Yx.Shared;
using Selection = YxpFinisherCalc.Selection;
using Analyzer = YxpFinisherCalc.Analyzer;

namespace YxpDefeatFinishers
{
    public sealed class FinisherMod : YxMod
    {
        private Session current;
        private ConstOverride[] delays;

        public FinisherMod() { }

        public override void OnLoad(ModContext context)
        {
            var hooks = context.Hooks.Group("defeat-finishers");
            hooks.Prefix("BattleExecuter", "Execute", 3, Execute);
            hooks.Postfix("BattleExecuter", "SetVisible", 1, Visible);
            hooks.Prefix("BattleCharacter", "OnTurnStarted", 0, TurnStarted);
            hooks.Prefix("CardActionBase", "Execute", 2, Card);
            hooks.Prefix("CardActionBase", "ExecuteEffect", 3, Effect);
            hooks.Prefix("BattleCharacter", "Attack", 4, Attack);
            hooks.Postfix("BattleExecuter", "ResurrectionCheckAsync", 1, Resurrected);
            hooks.Prefix("BattleCharacter", "MoveTo", 3, CompleteMovement);
            hooks.Prefix("BattleCharacter", "Return", 0, CompleteMovement);
            hooks.Prefix("BattleCharacter", "Cast", 4, Cast);
            hooks.Prefix("CharacterBattleAnimator", "PlayAnimation", 3, Animation);
            hooks.Prefix("CharacterBattleAnimator", "AddAnimation", 3, AddAnimation);
            hooks.Prefix("CharacterBattleAnimator", "WaitForEvent", 2, AnimationWait);
            hooks.Prefix("CharacterBattleAnimator", "WaitForEnd", 1, AnimationWait);
            hooks.Prefix("BattleExecuter", "PlaySound", 2, Sound);
            hooks.Prefix("BattleCharacter", "OnBlockSucceed", 0, BlockSucceeded);
            hooks.Prefix("BattleCharacter", "OnBlockFailed", 2, BlockFailed);
            hooks.Prefix("BattleCharacter", "OnBattleWin", 1, Won);
            hooks.Prefix("BattleCharacter", "SpawnDefeatEffect", 4, DefeatCosmetic);
            hooks.Prefix("BattleExecuter", "OnEnd", 1, Ended);
            hooks.Prefix("BattleExecuter", "OnDestroy", 0, Destroyed);
            hooks.Prefix("BattleManager", "OnDestroy", 0, ManagerDestroyed);
            hooks.Prefix("BattleManager", "ChangeSceneType", 2, SceneChanging);
            hooks.Prefix("CharacterBattleAnimatorPool", "Despawn", 1, AnimatorReleased);
            hooks.Prefix("CharacterBattleAnimatorPool", "ResetInstance", 2, AnimatorReleased);
            if (!hooks.Complete) throw new InvalidOperationException("Required finisher hooks unavailable: " + hooks.Missing);
            delays = FinisherDelays.Install(context.Hooks);
        }

        public override void OnUpdate()
        {
            Session session = current;
            if (session == null) return;
            if (!session.SameBattle() || !session.Executor.isVisible ||
                BattleManager.Instance == null || BattleManager.Instance.currentScene != SceneType.斗法阶段)
            {
                Clear();
                return;
            }
            if (session.Movie != null)
            {
                session.Movie.Tick();
                if (session.Movie.Closed && current == session) Clear();
            }
        }

        public override void OnDisable() { Clear(); DisableDelays(); }

        private void Prepare(BattleExecuter executor, BattleResult battle)
        {
            if (current != null && current.Executor == executor && current.Battle == battle) return;
            Clear();
            if (battle == null || executor.forceBreakExecuting) return;
            int first = battle.p1.publicData.characterId, second = battle.p2.publicData.characterId;
            if (first != 4000002 && first != 1000004 && second != 4000002 && second != 1000004) return;
            try
            {
                Selection selection = Analyzer.Analyze(battle, YxpFinisherCalc.Context.Capture(BattleManager.Instance));
                if (selection != null) current = new Session(this, executor, battle, selection);
            }
            catch (Exception exception) { Context.Log.Warn("Finisher calculation unavailable: " + exception.Message); }
        }

        private bool Execute(HookContext hook)
        {
            var executor = (BattleExecuter)hook.Instance;
            var battle = (BattleResult)hook.Args[0];
            if (battle == null || executor.isExecuting) return true;
            if (current != null && current.Executor == executor) Clear();
            if ((bool)hook.Args[2]) Prepare(executor, battle);
            return true;
        }

        private void Visible(HookContext hook)
        {
            var executor = (BattleExecuter)hook.Instance;
            if ((bool)hook.Args[0]) Prepare(executor, executor.currentBattleResult);
            else if (current != null && current.Executor == executor) Clear();
        }

        private bool TurnStarted(HookContext hook)
        {
            var source = (BattleCharacter)hook.Instance;
            Session session = current;
            if (session == null || source.battleExecuter != session.Executor || session.CallingTurn) return true;
            if (session.Movie != null) { Clear(); return true; }
            if (!session.SameBattle() || !session.Executor.isVisible ||
                DefeatFinisherPayload.ReadTurn(session.Executor) != session.Selected.Turn ||
                source.battleTempData.uid != session.Selected.WinnerUid ||
                source.defaultOpponentTarget.battleTempData.uid != session.Selected.LoserUid) return true;
            session.Source = source; session.Target = source.defaultOpponentTarget;
            session.SourceAnimator = source.animator; session.TargetAnimator = session.Target.animator;
            session.SourceTrack = session.SourceAnimator.skeletonAnimation.AnimationState.GetCurrent(0);
            session.Movie = new DefeatFinisherVisualsPayload.Playback(session.Executor, source, session.Target, session.Selected.Kind);
            if (!session.Movie.Start()) { Clear(); return true; }
            EnableDelays();
            var operation = new TurnOperation(session);
            hook.Skip(operation.Start());
            return false;
        }

        private bool Card(HookContext hook)
        {
            var source = (BattleCharacter)hook.Args[0];
            Session session = Playing(source);
            if (session == null) return true;
            var target = (BattleCharacter)hook.Args[1];
            var action = (CardActionBase)hook.Instance;
            int number = ++session.CardInTurn;
            int count = session.Selected.CardOrdinal - session.Selected.StartCardOrdinal + 1;
            if (source != session.Source || target != session.Target || number > count ||
                (number == count && session.Selected.Kind == 2 &&
                 CardFactory.GetBaseCardId(action.cardConfig.id) != session.Selected.FinishingCardBaseId))
            { Clear(); return true; }
            // ILRuntime maps CardActionExecuteResult to int. Windows 26939223
            // preserves UniTask<int>, but not UniTaskCompletionSource<int>.
            // Keep Execute's native result task and delay its non-generic
            // ExecuteEffect instead of constructing a second result task.
            return true;
        }

        private bool Effect(HookContext hook)
        {
            var source = (BattleCharacter)hook.Args[0];
            Session session = Playing(source);
            if (session == null || session.CallingEffect || source != session.Source || session.CardInTurn == 0) return true;
            bool last = session.CardInTurn == session.Selected.CardOrdinal - session.Selected.StartCardOrdinal + 1;
            var operation = new EffectOperation(session, (CardActionBase)hook.Instance, source,
                (BattleCharacter)hook.Args[1], (bool)hook.Args[2], last);
            hook.Skip(operation.Start());
            return false;
        }

        private bool Attack(HookContext hook)
        {
            var source = (BattleCharacter)hook.Instance;
            Session session = Playing(source);
            if (session == null) return true;
            var operation = new AttackOperation(session, source, (BattleCharacter)hook.Args[0], (List<int>)hook.Args[1]);
            hook.Skip(operation.Start());
            return false;
        }

        private void Resurrected(HookContext hook)
        {
            Session session = current;
            if (session == null || session.Executor != hook.Instance || session.Movie == null || session.Movie.Closed || session.Movie.Completed) return;
            hook.Result = new ResultOperation(session, (UniTask)hook.Result).Task;
        }

        private Session Playing(BattleCharacter character)
        {
            Session session = current;
            return session != null && character != null && character.battleExecuter == session.Executor &&
                session.Movie != null && !session.Movie.Closed && session.SameBattle() ? session : null;
        }

        private Session ForAnimator(CharacterBattleAnimator animator)
        {
            Session session = current;
            if (session == null || session.Movie == null || session.Movie.Closed || !session.SameBattle()) return null;
            return session.SourceAnimator == animator || session.TargetAnimator == animator ? session : null;
        }

        private bool CompleteMovement(HookContext hook)
        {
            if (Playing((BattleCharacter)hook.Instance) == null) return true;
            hook.Skip(UniTask.CompletedTask); return false;
        }
        private bool Cast(HookContext hook)
        {
            Session session = Playing((BattleCharacter)hook.Instance);
            if (session == null || session.Source != hook.Instance) return true;
            hook.Skip(session.SourceTrack); return false;
        }
        private bool Animation(HookContext hook)
        {
            Session session = ForAnimator((CharacterBattleAnimator)hook.Instance);
            if (session == null || session.SourceAnimator != hook.Instance) return true;
            hook.Skip(session.SourceTrack); return false;
        }
        private bool AddAnimation(HookContext hook)
        {
            Session session = ForAnimator((CharacterBattleAnimator)hook.Instance);
            if (session == null || session.SourceAnimator != hook.Instance) return true;
            hook.Skip(null); return false;
        }
        private bool AnimationWait(HookContext hook)
        {
            if (ForAnimator((CharacterBattleAnimator)hook.Instance) == null) return true;
            hook.Skip(UniTask.CompletedTask); return false;
        }
        private bool Sound(HookContext hook)
        {
            if (current == null || current.Executor != hook.Instance || current.Movie == null || current.Movie.Closed) return true;
            hook.Skip(null); return false;
        }

        private bool BlockSucceeded(HookContext hook)
        {
            var character = (BattleCharacter)hook.Instance;
            if (Playing(character) == null) return true;
            if (!character.m_IsDead)
            {
                character.battleExecuter.PlaySound("game_击中护盾");
                TrackEntry track = character.animator.PlayAnimation(CharacterAnimClipType.Battle_Block, -1, false);
                character.animator.AddAnimation(track, CharacterAnimClipType.Battle_BlockReady);
                Shake(character.animator);
            }
            hook.Skip(null); return false;
        }
        private bool BlockFailed(HookContext hook)
        {
            var character = (BattleCharacter)hook.Instance;
            if (Playing(character) == null) return true;
            character.battleExecuter.PlaySound("game_击中（剑）");
            if (!character.m_IsDead)
            {
                character.animator.PlayAnimation(CharacterAnimClipType.Battle_Hurt, -1, (bool)hook.Args[1]);
                Shake(character.animator);
            }
            hook.Skip(null); return false;
        }
        private static void Shake(CharacterBattleAnimator animator)
        {
            animator.transform.DOKill();
            animator.transform.DOLocalMoveX(0.15f * -animator.facingDirection.x, 0.05f).SetRelative().SetLoops(4, LoopType.Yoyo)
                .OnKill(delegate { animator.transform.localPosition = Vector3.zero; });
        }
        private bool Won(HookContext hook)
        {
            var winner = (BattleCharacter)hook.Instance;
            Session session = Playing(winner);
            if (session == null || session.Source != winner) return true;
            if (!winner.m_IsDead)
            {
                if (winner.battleExecuter.isVisible) Time.timeScale = 1f;
                winner.transform.DOKill();
                if (winner.characterUI.tempLife <= 0)
                    new NativeCompletion(UniTask.Delay(1800), delegate { winner.ShowDeathDamage(0); }, session.Fail);
            }
            hook.Skip(null); return false;
        }
        private bool DefeatCosmetic(HookContext hook)
        {
            var loser = (BattleCharacter)hook.Instance;
            if (Playing(loser) == null) return true;
            // OnBattleLose's equipped branch skips its ordinary 1500ms delay;
            // each absent cosmetic supplies that same delay to its WhenAll.
            hook.Skip(UniTask.Delay(1500)); return false;
        }

        private bool Ended(HookContext hook)
        {
            if (current != null && current.Executor == hook.Instance &&
                (current.Executor.forceBreakExecuting || current.Movie == null || !current.Movie.Completed || current.Movie.Closed)) Clear();
            return true;
        }
        private bool Destroyed(HookContext hook)
        {
            if (current != null && current.Executor == hook.Instance) Clear();
            return true;
        }
        private bool ManagerDestroyed(HookContext hook) { Clear(); return true; }
        private bool SceneChanging(HookContext hook)
        {
            if ((SceneType)hook.Args[0] != SceneType.斗法阶段) Clear();
            return true;
        }
        private bool AnimatorReleased(HookContext hook)
        {
            var animator = (CharacterBattleAnimator)hook.Args[0];
            // Pool reuse can follow currentBattleResult replacement. Compare
            // the exact borrowed actors even after the old result is obsolete.
            if (current != null && (current.SourceAnimator == animator || current.TargetAnimator == animator)) Clear();
            return true;
        }

        private void EnableDelays()
        {
            if (delays == null) return;
            for (int i = 0; i < delays.Length; i++) delays[i].Set(0);
        }
        internal void DisableDelays()
        {
            if (delays == null) return;
            for (int i = 0; i < delays.Length; i++) delays[i].Disable();
        }
        private void Clear()
        {
            Session old = current;
            current = null;
            DisableDelays();
            if (old != null && old.Movie != null) old.Movie.Dispose();
        }

        private sealed class Session
        {
            internal readonly FinisherMod Mod;
            internal readonly BattleExecuter Executor;
            internal readonly BattleResult Battle;
            internal readonly Selection Selected;
            internal BattleCharacter Source, Target;
            internal CharacterBattleAnimator SourceAnimator, TargetAnimator;
            internal TrackEntry SourceTrack;
            internal DefeatFinisherVisualsPayload.Playback Movie;
            internal bool CallingTurn, CallingEffect;
            internal int CardInTurn;
            internal Session(FinisherMod mod, BattleExecuter executor, BattleResult battle, Selection selected)
            { Mod = mod; Executor = executor; Battle = battle; Selected = selected; }
            internal bool SameBattle()
            { return Executor.currentBattleResult == Battle && !Executor.forceBreakExecuting; }
            internal bool CanCalculate()
            { return SameBattle() && Executor.isExecuting && BattleManager.Instance != null && BattleManager.Instance.currentScene == SceneType.斗法阶段; }
            internal void Fail(Exception exception)
            {
                if (Mod.current == this) Mod.Clear();
                Mod.Context.Log.Error("Finisher native operation failed: " + exception.Message);
            }
            internal bool HasEnded()
            {
                if (!CanCalculate() || CardInTurn != Selected.CardOrdinal - Selected.StartCardOrdinal + 1) return false;
                BattleCharacter left = Executor.leftCharacter, right = Executor.rightCharacter;
                int leftHp = left.battleTempData.hp, rightHp = right.battleTempData.hp;
                // Read the result of CheckSiZhan without performing its buff
                // writes. The original DeathCheck runs those writes once when
                // this awaited resurrection wrapper returns.
                bool leftStands = left.HasBuff(BuffType.SiZhanBuDao) || leftHp <= 0 && left.HasBuff(BuffType.SiZhanZhiZhi);
                bool rightStands = right.HasBuff(BuffType.SiZhanBuDao) || rightHp <= 0 && right.HasBuff(BuffType.SiZhanZhiZhi);
                if (leftStands && leftHp <= rightHp || rightStands && rightHp <= leftHp) return false;
                return Target.battleTempData.hp <= 0;
            }
        }

        private sealed class TurnOperation
        {
            private readonly Session session;
            private readonly UniTaskCompletionSource done = new UniTaskCompletionSource();
            internal TurnOperation(Session session) { this.session = session; }
            internal UniTask Start() { session.Movie.BeforeCard(false, Run); return done.Task; }
            private void Run()
            {
                try
                {
                    if (!session.CanCalculate()) { done.TrySetResult(); return; }
                    session.CallingTurn = true;
                    UniTask native;
                    try { native = session.Source.OnTurnStarted(); }
                    finally { session.CallingTurn = false; }
                    new NativeCompletion(native, Complete, Fail);
                }
                catch (Exception exception) { Fail(exception); }
            }
            private void Complete() { done.TrySetResult(); }
            private void Fail(Exception exception) { session.Fail(exception); done.TrySetException(exception); }
        }

        private sealed class EffectOperation
        {
            private readonly Session session;
            private readonly CardActionBase action;
            private readonly BattleCharacter source, target;
            private readonly bool isTempCard, last;
            private readonly UniTaskCompletionSource done = new UniTaskCompletionSource();
            internal EffectOperation(Session session, CardActionBase action, BattleCharacter source, BattleCharacter target, bool isTempCard, bool last)
            { this.session = session; this.action = action; this.source = source; this.target = target; this.isTempCard = isTempCard; this.last = last; }
            internal UniTask Start() { session.Movie.BeforeCard(last, Run); return done.Task; }
            private void Run()
            {
                try
                {
                    if (!session.CanCalculate()) { done.TrySetResult(); return; }
                    UniTask native;
                    session.CallingEffect = true;
                    try { native = action.ExecuteEffect(source, target, isTempCard); }
                    finally { session.CallingEffect = false; }
                    new NativeCompletion(native, Complete, Fail);
                }
                catch (Exception exception) { Fail(exception); }
            }
            private void Complete() { done.TrySetResult(); }
            private void Fail(Exception exception) { session.Fail(exception); done.TrySetException(exception); }
        }

        private sealed class AttackOperation
        {
            private readonly Session session;
            private readonly BattleCharacter source, target;
            private readonly List<int> attacks;
            private readonly int count;
            private int index;
            private bool pumping, hitReady;
            private readonly UniTaskCompletionSource done = new UniTaskCompletionSource();
            internal AttackOperation(Session session, BattleCharacter source, BattleCharacter target, List<int> attacks)
            { this.session = session; this.source = source; this.target = target; this.attacks = attacks; count = attacks.Count; }
            internal UniTask Start()
            {
                try { target.BeginBlock(); Pump(); }
                catch (Exception exception) { Fail(exception); }
                return done.Task;
            }
            private void Ready()
            {
                hitReady = true;
                if (!pumping) Pump();
            }
            private void Pump()
            {
                pumping = true;
                try
                {
                    while (session.CanCalculate() && attacks.Count > 0)
                    {
                        if (!hitReady)
                        {
                            if (session.Movie.Closed) hitReady = true;
                            else session.Movie.BeforeHit(index, count, Ready);
                            if (!hitReady) return;
                        }
                        hitReady = false;
                        if (!session.CanCalculate()) break;
                        source.CalculateAttack(target, attacks[0]);
                        attacks.RemoveAt(0); index++;
                    }
                    target.EndBlock(); done.TrySetResult();
                }
                catch (Exception exception) { Fail(exception); }
                finally { pumping = false; }
            }
            private void Fail(Exception exception) { session.Fail(exception); done.TrySetException(exception); }
        }

        private sealed class ResultOperation
        {
            private readonly Session session;
            private readonly UniTaskCompletionSource done = new UniTaskCompletionSource();
            internal ResultOperation(Session session, UniTask native)
            { this.session = session; new NativeCompletion(native, Resolved, Fail); }
            internal UniTask Task { get { return done.Task; } }
            private void Resolved()
            {
                try
                {
                    if (session.Mod.current != session || session.Movie.Closed || !session.HasEnded()) { Complete(); return; }
                    // Direct HP loss can bypass OnHit's ordinary death reaction.
                    // This is the same idempotent native presentation call that
                    // OnBattleLose will make after this await has returned.
                    session.Target.OnDead();
                    session.Movie.AfterCard(true);
                    session.Movie.Finish(Complete);
                }
                catch (Exception exception) { Fail(exception); }
            }
            private void Complete()
            {
                if (session.Mod.current == session && (session.Movie.Completed || session.Movie.Closed)) session.Mod.DisableDelays();
                done.TrySetResult();
            }
            private void Fail(Exception exception) { session.Fail(exception); done.TrySetException(exception); }
        }

        private sealed class NativeCompletion
        {
            private UniTask.Awaiter native;
            private readonly Action complete;
            private readonly Action<Exception> fail;
            internal NativeCompletion(UniTask task, Action complete, Action<Exception> fail)
            {
                this.complete = complete; this.fail = fail; native = task.GetAwaiter();
                if (native.IsCompleted) Resume(); else native.OnCompleted(Resume);
            }
            private void Resume()
            {
                try { native.GetResult(); complete(); }
                catch (Exception exception) { fail(exception); }
            }
        }
    }
}
