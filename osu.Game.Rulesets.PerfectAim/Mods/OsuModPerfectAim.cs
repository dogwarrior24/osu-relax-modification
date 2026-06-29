// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.PerfectAim.Mods
{
    /// <summary>
    /// A stricter relative of <see cref="OsuModRelax"/>. The player never clicks; instead a hit is
    /// only registered for a hit circle if the cursor is hovering it within the "Great" (300) timing
    /// window around the object's perfect hit time. Aiming the circle too early or too late results
    /// in a miss, so the only possible circle judgements are 300 or miss. Sliders and spinners keep
    /// relax-style auto-handling so maps remain completable; the aim-timing challenge is focused on
    /// tappable circles.
    /// </summary>
    public class OsuModPerfectAim : Mod, IUpdatableByPlayfield, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableToPlayer, IHasNoTimedInputs
    {
        public override string Name => "Perfect Aim";
        public override string Acronym => "PA";
        public override LocalisableString Description => @"No clicking — but a circle only counts if your cursor is on it at the perfect moment. Pure aim-timing practice.";
        public override ModType Type => ModType.Automation;
        public override IconUsage? Icon => FontAwesome.Solid.Bullseye;

        public override Type[] IncompatibleMods => new[]
        {
            typeof(ModAutoplay),
            typeof(OsuModRelax),
            typeof(OsuModAutopilot),
            typeof(OsuModMagnetised),
            typeof(OsuModAlternate),
            typeof(OsuModSingleTap),
        };

        [SettingSource("Timing strictness", "How close to perfect timing the cursor must be, as a fraction of the 300 (Great) hit window. Lower is stricter.")]
        public BindableNumber<double> WindowScale { get; } = new BindableDouble(1)
        {
            MinValue = 0.1,
            MaxValue = 1,
            Precision = 0.05,
        };

        /// <summary>
        /// How far before an object's start time we begin considering it for activation.
        /// Must comfortably cover the widest possible "Great" window (osu!'s is at most ~80ms, at OD0)
        /// so that early-but-perfect circle aims are never clipped. <see cref="WindowScale"/> is
        /// capped at 1, so the effective trigger window can never exceed this.
        /// </summary>
        private const double max_activation_leniency = 80;

        private bool isDownState;
        private bool wasLeft;

        private OsuInputManager osuInputManager = null!;

        private ReplayState<OsuAction> state = null!;
        private double lastStateChangeTime;

        private DrawableOsuRuleset ruleset = null!;
        private IPressHandler pressHandler = null!;

        private bool hasReplay;
        private bool legacyReplay;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            ruleset = (DrawableOsuRuleset)drawableRuleset;

            // grab the input manager for future use.
            osuInputManager = ruleset.KeyBindingInputManager;
        }

        public void ApplyToPlayer(Player player)
        {
            if (osuInputManager.ReplayInputHandler != null)
            {
                hasReplay = true;

                Debug.Assert(ruleset.ReplayScore != null);
                legacyReplay = ruleset.ReplayScore.ScoreInfo.IsLegacyScore;

                pressHandler = legacyReplay ? new LegacyReplayPressHandler(this) : new PressHandler(this);

                return;
            }

            pressHandler = new PressHandler(this);
            osuInputManager.AllowGameplayInputs = false;
        }

        public void Update(Playfield playfield)
        {
            if (hasReplay && !legacyReplay)
                return;

            bool requiresHold = false;
            bool requiresHit = false;

            double time = playfield.Clock.CurrentTime;

            foreach (var h in playfield.HitObjectContainer.AliveObjects.OfType<DrawableOsuHitObject>())
            {
                // we are not yet close enough to the object (or any later one).
                if (time < h.HitObject.StartTime - max_activation_leniency)
                    break;

                // already hit or beyond the hittable end time.
                if (h.IsHit || (h.HitObject is IHasDuration hasEnd && time > hasEnd.EndTime))
                    continue;

                switch (h)
                {
                    case DrawableHitCircle circle:
                        // strict timing: only register circles that are aimed at the perfect moment.
                        handlePerfectHitCircle(circle);
                        break;

                    case DrawableSlider slider:
                        // slider heads keep relax-style behaviour so that maps stay completable.
                        if (!slider.HeadCircle.IsHit)
                            handleRelaxHitCircle(slider.HeadCircle);

                        requiresHold |= slider.SliderInputManager.IsMouseInFollowArea(slider.Tracking.Value);
                        break;

                    case DrawableSpinner spinner:
                        requiresHold |= spinner.HitObject.SpinsRequired > 0;
                        break;
                }
            }

            if (requiresHit)
            {
                changeState(false);
                changeState(true);
            }

            if (requiresHold)
                changeState(true);
            else if (isDownState && time - lastStateChangeTime > AutoGenerator.KEY_UP_DELAY)
                changeState(false);

            void handlePerfectHitCircle(DrawableHitCircle circle)
            {
                Debug.Assert(circle.HitObject.HitWindows != null);

                double timeOffset = time - circle.HitObject.StartTime;
                double window = circle.HitObject.HitWindows.WindowFor(HitResult.Great) * WindowScale.Value;

                // outside the perfect-timing window: either too early to press yet, or the perfect
                // moment has already passed (in which case the circle is intentionally left to miss).
                if (timeOffset < -window || timeOffset > window)
                    return;

                if (!circle.HitArea.IsHovered)
                    return;

                requiresHit = true;
            }

            void handleRelaxHitCircle(DrawableHitCircle circle)
            {
                if (!circle.HitArea.IsHovered)
                    return;

                Debug.Assert(circle.HitObject.HitWindows != null);
                requiresHit |= circle.HitObject.HitWindows.CanBeHit(time - circle.HitObject.StartTime);
            }

            void changeState(bool down)
            {
                if (isDownState == down)
                    return;

                isDownState = down;
                lastStateChangeTime = time;

                state = new ReplayState<OsuAction>
                {
                    PressedActions = new List<OsuAction>()
                };

                if (down)
                {
                    pressHandler.HandlePress(wasLeft);
                    wasLeft = !wasLeft;
                }
                else
                {
                    pressHandler.HandleRelease(wasLeft);
                }
            }
        }

        private interface IPressHandler
        {
            void HandlePress(bool wasLeft);
            void HandleRelease(bool wasLeft);
        }

        private class PressHandler : IPressHandler
        {
            private readonly OsuModPerfectAim mod;

            public PressHandler(OsuModPerfectAim mod)
            {
                this.mod = mod;
            }

            public void HandlePress(bool wasLeft)
            {
                mod.state.PressedActions.Add(wasLeft ? OsuAction.LeftButton : OsuAction.RightButton);
                mod.state.Apply(mod.osuInputManager.CurrentState, mod.osuInputManager);
            }

            public void HandleRelease(bool wasLeft)
            {
                mod.state.Apply(mod.osuInputManager.CurrentState, mod.osuInputManager);
            }
        }

        // legacy replays do not contain key-presses with relax-style mods, so they need to be triggered by themselves.
        private class LegacyReplayPressHandler : IPressHandler
        {
            private readonly OsuModPerfectAim mod;

            public LegacyReplayPressHandler(OsuModPerfectAim mod)
            {
                this.mod = mod;
            }

            public void HandlePress(bool wasLeft)
            {
                mod.osuInputManager.KeyBindingContainer.TriggerPressed(wasLeft ? OsuAction.LeftButton : OsuAction.RightButton);
            }

            public void HandleRelease(bool wasLeft)
            {
                // this intentionally releases right when `wasLeft` is true because `wasLeft` is set at point of press and not at point of release
                mod.osuInputManager.KeyBindingContainer.TriggerReleased(wasLeft ? OsuAction.RightButton : OsuAction.LeftButton);
            }
        }
    }
}
