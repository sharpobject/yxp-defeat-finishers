using System;
using Proto;
using UnityEngine;

internal static partial class DefeatFinisherVisualsPayload
{
    // Native combat awaits one cue at a time. The loader advances the same
    // authored movie clock from its frame callback; no async state machine or
    // independent task queue is needed in the interpreted plugin.
    internal sealed class Playback
    {
        private readonly BattleExecuter executor;
        private readonly BattleCharacter winner, loser;
        private readonly int kind;
        private readonly BattleResult result;
        private readonly Movie movie = new Movie();
        private bool started, closed, completed, finalRequested, finalDone, currentFinal, paused;
        private float elapsed, previous, lastHit, heldSince;
        private Action cueReady, finishReady;
        private float pendingCue;
        private bool pendingImpact;

        public bool Closed { get { return closed; } }
        public bool Completed { get { return completed; } }

        public Playback(BattleExecuter executor, BattleCharacter winner, BattleCharacter loser, int kind)
        {
            this.executor = executor; this.winner = winner; this.loser = loser; this.kind = kind;
            result = executor.currentBattleResult;
        }

        public bool Start()
        {
            try
            {
                if (!Alive(executor, result)) { closed = true; return false; }
                movie.Create(executor, winner, loser, kind);
                movie.LoadSound(kind);
                previous = Time.realtimeSinceStartup;
                movie.sound.Play();
                started = true;
                Draw(0f);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("Defeat finisher presentation failed: " + exception.Message);
                Dispose();
                return false;
            }
        }

        public void Tick()
        {
            if (!started || closed) return;
            if (completed ? !StillShown() : !Alive(executor, result)) { Dispose(); return; }
            try
            {
                float now = Time.realtimeSinceStartup;
                if (completed) { movie.DrawHeld(now - heldSince); return; }
                float next = elapsed + now - previous;
                previous = now;
                float duration = kind == 1 ? 6.8f : LinDuration;
                float limit = kind == 1 ? (finalDone ? duration : 2.75f) :
                    (!finalRequested ? LinKnifeHit + 0.19f : !finalDone ? LinRollerHit + 0.16f : duration);
                bool hold = next > limit && !(finalDone && limit == duration);
                elapsed = Mathf.Min(next, limit);
                if (hold && !paused) { movie.sound.Pause(); movie.sound.time = elapsed; paused = true; }
                else if (!hold && paused) { movie.sound.UnPause(); paused = false; }
                Draw(elapsed);
                movie.UpdateImpact(now);
                if (finalDone && elapsed >= duration)
                {
                    movie.Hold(); heldSince = now; completed = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Defeat finisher presentation failed: " + exception.Message);
                Dispose();
                return;
            }
            // Release native work outside the drawing exception handler. Each
            // caller owns its native awaiter's completion and failure result.
            ReleaseCue();
            if (completed) ReleaseFinish();
        }

        private void Draw(float time)
        {
            if (kind == 1) movie.Demon(time); else movie.Roller(time);
        }

        public void BeforeCard(bool finalCard, Action ready)
        {
            currentFinal = finalCard;
            if (finalCard) finalRequested = true;
            WaitCue(kind == 1 ? 1.7f : finalCard ? LinRollerHit : LinKnifeHit, false, ready);
        }

        public void BeforeHit(int index, int count, Action ready)
        {
            float start = kind == 1 ? 1.7f : currentFinal ? LinRollerHit : LinKnifeHit;
            float span = kind == 1 ? 1.05f : currentFinal ? 0.16f : 0.19f;
            int beat = index * 15 / Mathf.Max(1, count);
            int previousBeat = (index - 1) * 15 / Mathf.Max(1, count);
            WaitCue(start + span * index / Mathf.Max(1, count - 1),
                index == 0 || beat != previousBeat, ready);
        }

        private void WaitCue(float cue, bool impact, Action ready)
        {
            if (closed) { ready(); return; }
            if (cueReady != null) throw new InvalidOperationException("Native finisher cues overlap.");
            pendingCue = cue; pendingImpact = impact; cueReady = ready;
            ReleaseCue();
        }

        private void ReleaseCue()
        {
            if (cueReady == null || elapsed + 0.0001f < pendingCue ||
                pendingImpact && Time.realtimeSinceStartup < lastHit + 0.045f) return;
            Action ready = cueReady;
            cueReady = null;
            if (pendingImpact)
            {
                lastHit = Time.realtimeSinceStartup;
                movie.Impact(currentFinal);
            }
            ready();
        }

        public void AfterCard(bool finalCard)
        {
            if (!finalCard) return;
            finalDone = true;
            if (!closed) movie.HideDefeatedStatusBar();
        }

        public void Finish(Action ready)
        {
            if (!started || closed || completed) { ready(); return; }
            if (finishReady != null) throw new InvalidOperationException("Native finisher results overlap.");
            finishReady = ready;
        }

        private void ReleaseFinish()
        {
            Action ready = finishReady;
            finishReady = null;
            if (ready != null) ready();
        }

        private bool StillShown()
        {
            return executor != null && executor.transform != null && executor.isVisible &&
                !executor.forceBreakExecuting && executor.currentBattleResult == result;
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            movie.Dispose();
            Action ready = cueReady;
            cueReady = null;
            if (ready != null) ready();
            ReleaseFinish();
        }
    }
}
