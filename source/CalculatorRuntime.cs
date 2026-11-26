using System;
using Proto;

namespace YxpFinisherCalc
{
    // This is all the visible player needs from the detached calculation.
    public sealed class Selection
    {
        public int Kind;
        public int Turn;
        public int CardOrdinal;
        public int StartCardOrdinal;
        public string WinnerUid;
        public string LoserUid;
        public int FinishingCardBaseId;
    }

    public sealed class SelectionCollector
    {
        public int TurnCount { get; private set; }
        private int cardOrdinal;
        private int turnFirstCardOrdinal;
        private CardActionBase activeCard;
        private BattleCharacter activeSource;
        private BattleCharacter activeTarget;
        private int cardStartAttackCount;
        private bool cardConsumedGuard;
        private bool priorCardQualified;
        private bool tuQualifyingCardPlayed;
        private CardActionBase directAttack;
        private BattleCharacter directSource;
        private BattleCharacter directTarget;
        private Selection candidate;
        private bool cardEndedBattle;

        public void TurnStarted(int turn, BattleCharacter source, BattleCharacter target)
        {
            TurnCount = turn;
            turnFirstCardOrdinal = cardOrdinal + 1;
            priorCardQualified = false;
            tuQualifyingCardPlayed = false;
            candidate = null;
            cardEndedBattle = false;
            activeCard = null;
            directAttack = null;
        }

        public void CardStarted(CardActionBase action, BattleCharacter source, BattleCharacter target)
        {
            cardOrdinal++;
            activeCard = action;
            activeSource = source;
            activeTarget = target;
            cardStartAttackCount = source.GetBuffValue(BuffType.ZhanDouGongJiJiShu);
            cardConsumedGuard = false;
        }

        public void GuardConsumed(BattleCharacter target)
        {
            // Called after ModifyBuffValue applies an actual HuTi decrease.
            // A source's own reflected-damage block does not qualify its card.
            if (activeCard != null && target == activeTarget)
                cardConsumedGuard = true;
        }

        public void CardEnded(CardActionBase action, BattleCharacter source, BattleCharacter target)
        {
            // CalculateAttack already increments this native battle-total count.
            // Reading its difference includes extra attacks from Cat Shadow and
            // repeated effects, without counting reflected/direct passive damage.
            if (activeCard == action && source == activeSource && cardConsumedGuard &&
                source.GetBuffValue(BuffType.ZhanDouGongJiJiShu) - cardStartAttackCount >= 2)
                priorCardQualified = true;
            if (activeCard == action && source == activeSource &&
                source.battleTempData.playerData.publicData.characterId == 4000002)
            {
                int card = CardFactory.GetBaseCardId(action.cardConfig.id);
                // Execute is reached only after native skip/cost checks. The
                // card may finish without killing; later attacks, chase damage
                // or turn-end effects can provide this turn's actual elimination.
                if (card == 10000047 || card == 339) tuQualifyingCardPlayed = true;
                if (tuQualifyingCardPlayed)
                    candidate = new Selection
                    {
                        Kind = 1, Turn = TurnCount, CardOrdinal = cardOrdinal,
                        StartCardOrdinal = turnFirstCardOrdinal,
                        WinnerUid = source.battleTempData.uid, LoserUid = target.battleTempData.uid,
                        FinishingCardBaseId = card,
                    };
            }
            activeCard = null;
            activeSource = null;
            activeTarget = null;
            directAttack = null;
        }

        public void DirectAttackStarted(CardActionBase action, BattleCharacter source, BattleCharacter target)
        {
            directAttack = action;
            directSource = source;
            directTarget = target;
        }

        public void DirectAttackEnded(CardActionBase action, BattleCharacter source, BattleCharacter target)
        {
            directAttack = null;
            directSource = null;
            directTarget = null;
        }

        public void HpChanged(BattleCharacter target)
        {
            if (candidate != null && candidate.Kind == 2 && target.battleTempData.uid == candidate.LoserUid &&
                target.battleTempData.hp > 0)
                candidate = null;
        }

        public void Hit(BattleCharacter target, DamageInfo damage, int hpBefore)
        {
            if (activeCard == null || directAttack == null || directSource != activeSource ||
                target != activeTarget || target != directTarget || damage.source != activeSource ||
                damage.type != DamageType.Attack || hpBefore <= 0 || target.battleTempData.hp > 0)
                return;
            int card = CardFactory.GetBaseCardId(directAttack.cardConfig.id);
            // A copied/nested effect is still the displayed parent card. Only
            // select the requested card when it is the actual played card too.
            if (CardFactory.GetBaseCardId(activeCard.cardConfig.id) != card)
                return;
            int character = activeSource.battleTempData.playerData.publicData.characterId;
            int kind = character == 1000004 && card == 1000045 && priorCardQualified ? 2 : 0;
            if (kind == 0) return;
            candidate = new Selection
            {
                Kind = kind,
                Turn = TurnCount,
                CardOrdinal = cardOrdinal,
                StartCardOrdinal = turnFirstCardOrdinal,
                WinnerUid = activeSource.battleTempData.uid,
                LoserUid = target.battleTempData.uid,
                FinishingCardBaseId = card,
            };
            cardEndedBattle = false;
        }

        public bool CardResolved(bool ended)
        {
            if (!ended && candidate != null && candidate.Kind == 2) candidate = null;
            cardEndedBattle = ended;
            return ended;
        }

        public Selection Finish(BattleResult result, BattleCharacter winner, BattleCharacter loser, int remainingDestiny)
        {
            if (candidate == null || candidate.Kind == 2 && !cardEndedBattle || candidate.WinnerUid != winner.battleTempData.uid ||
                candidate.LoserUid != loser.battleTempData.uid || loser.battleTempData.hp > 0 ||
                candidate.CardOrdinal != cardOrdinal || candidate.Turn != TurnCount)
                return null;
            // Completed records supply post-battle Destiny; fresh practice
            // battles supply the native defeat calculation's remaining Destiny.
            // Inner demons and survival talents retaining 1 do not qualify.
            var loserData = result.p1.publicData.uid == candidate.LoserUid ? result.p1.publicData : result.p2.publicData;
            if (loserData.isMirror || loserData.lastRoundData.life <= 0 || remainingDestiny > 0)
                return null;
            return candidate;
        }
    }

    public static class Analyzer
    {
        public static Selection Analyze(BattleResult battle, GameMode gameMode, bool isPlayingAllBattles)
        {
            return Analyze(battle, new Context(gameMode, battle == null ? 0 : battle.round)
            {
                isPlayingAllBattles = isPlayingAllBattles,
            });
        }

        public static Selection Analyze(BattleResult battle, Context context)
        {
            if (context == null || battle == null || battle.p1 == null || battle.p2 == null ||
                battle.p1.publicData == null || battle.p2.publicData == null ||
                battle.p1.privateData == null || battle.p2.privateData == null ||
                battle.p1.publicData.lastRoundData == null || battle.p2.publicData.lastRoundData == null)
                return null;
            // The practice editor supplies starting players and a random stream,
            // with no completed result. The live manager and the input must both
            // identify practice; incomplete recorded battles keep being rejected.
            bool freshPractice = context.gameMode == GameMode.PracticeMode &&
                battle.gameMode == GameMode.PracticeMode && string.IsNullOrEmpty(battle.winerId) &&
                battle.huiHeCount == 0 && battle.hpDelta == 0;
            if (!freshPractice && string.IsNullOrEmpty(battle.winerId)) return null;
            var executor = new BattleExecuter { context = context };
            var inputs = InputCopy.Copy(battle);
            executor.Execute(inputs, visible: false);
            var left = executor.leftCharacter;
            var right = executor.rightCharacter;
            if (left.battleTempData == null || right.battleTempData == null || executor.isExecuting)
                return null;
            int delta = left.battleTempData.hp - right.battleTempData.hp;
            var winner = delta > 0 ? left : delta < 0 ? right :
                left.battleTempData.uid == battle.firstPlayerId ? left : right;
            var loser = winner == left ? right : left;
            int remainingDestiny;
            if (freshPractice)
            {
                // Practice preallocates random choices, so an unused suffix is
                // expected. Missing choices still throw in the native reader.
                // These retained native operations include Destiny reductions,
                // immunity and survival talents. They update only the detached
                // character's tempLife, leaving the input record untouched.
                loser.ShowDeathDamage(executor.CalLifeDamage(winner, loser));
                remainingDestiny = loser.characterUI.tempLife;
            }
            else
            {
                // Completed records must agree on turn, consumed random choices,
                // winner and absolute HP separation (presentation can swap sides).
                if (executor.battleParamsQueue.Count != 0 || executor.selection.TurnCount != battle.huiHeCount ||
                    winner.battleTempData.uid != battle.winerId || Math.Abs(delta) != Math.Abs(battle.hpDelta))
                    return null;
                remainingDestiny = loser.battleTempData.playerData.publicData.life;
            }
            return executor.selection.Finish(battle, winner, loser, remainingDestiny);
        }
    }

    public sealed class Context
    {
        public readonly GameMode gameMode;
        public GameMode reviewOrgionMode;
        public GameMode replayGameMode;
        public bool hasGameStatus;
        public int statusRound;
        public bool isVersusRelateAll;
        public string yiXianLuId;
        // Execute has started this detached calculation. Playback scene changes
        // and cancellation remain the responsibility of the real executor.
        public SceneType currentScene = SceneType.斗法阶段;
        public bool replaying = true;
        public bool isJudge;
        public bool isPlayingAllBattles;
        public bool isTowerMultiMode;
        public bool isTA50Mode;
        public Context(GameMode mode, int round)
        {
            gameMode = mode;
            hasGameStatus = true;
            statusRound = round;
            isVersusRelateAll = mode == GameMode.VersusMode;
        }
        public static Context Capture(global::BattleManager manager)
        {
            var status = manager.currentGameStatus;
            return new Context(manager.gameMode, status == null ? 0 : status.round)
            {
                reviewOrgionMode = manager.reviewOrgionMode,
                replayGameMode = global::BattleManager.replayGameMode,
                replaying = manager.replaying,
                isPlayingAllBattles = manager.isPlayingAllBattles,
                isJudge = manager.isJudge,
                isTowerMultiMode = manager.isTowerMultiMode,
                isTA50Mode = manager.isTA50Mode,
                hasGameStatus = status != null,
                isVersusRelateAll = global::GameStatusExtension.IsVersusRelateAll(status),
                yiXianLuId = status == null || status.battleYiXianLuData == null ? null : status.battleYiXianLuData.yiXianLuId,
            };
        }
        public void ChangeSceneType(SceneType value) { currentScene = value; }
        public void SwitchCamera(SceneType value, bool useTween) { }
    }

    public static class NoOp
    {
        public static void Run() { }
    }

    public static class OracleTrace
    {
        public static void Error(object value)
        {
            if (value is Exception exception) throw new InvalidOperationException("Combat calculation failed", exception);
            throw new InvalidOperationException("Combat calculation: " + value);
        }
        public static int RandomFallback(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("Combat calculation exhausted the recorded random outcomes");
    }
}
