﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Screens.Play.HUD.HitErrorMeters
{
    [Cached]
    public class BarHitErrorMeter : HitErrorMeter
    {
        private const int judgement_line_width = 14;

        [SettingSource("Judgement line thickness", "How thick the individual lines should be.")]
        public BindableNumber<float> JudgementLineThickness { get; } = new BindableNumber<float>(4)
        {
            MinValue = 1,
            MaxValue = 8,
            Precision = 0.1f,
        };

        [SettingSource("Show moving average arrow", "Whether an arrow should move beneath the bar showing the average error.")]
        public Bindable<bool> ShowMovingAverage { get; } = new BindableBool(true);

        [SettingSource("Centre marker style", "How to signify the centre of the display")]
        public Bindable<CentreMarkerStyles> CentreMarkerStyle { get; } = new Bindable<CentreMarkerStyles>(CentreMarkerStyles.Circle);

        [SettingSource("Label style", "How to show early/late extremities")]
        public Bindable<LabelStyles> LabelStyle { get; } = new Bindable<LabelStyles>(LabelStyles.Icons);

        private readonly DrawablePool<JudgementLine> judgementLinePool = new DrawablePool<JudgementLine>(50, 100);

        private SpriteIcon arrow;
        private UprightAspectMaintainingContainer labelEarly;
        private UprightAspectMaintainingContainer labelLate;

        private Container colourBarsEarly;
        private Container colourBarsLate;

        private Container judgementsContainer;

        private double maxHitWindow;

        private double floatingAverage;
        private Container colourBars;
        private Container arrowContainer;

        private (HitResult result, double length)[] hitWindows;

        private const int max_concurrent_judgements = 50;

        private Drawable[] centreMarkerDrawables;

        private const int centre_marker_size = 8;

        public BarHitErrorMeter()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            const int bar_height = 200;
            const int bar_width = 2;
            const float chevron_size = 8;

            hitWindows = HitWindows.GetAllAvailableWindows().ToArray();

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.X,
                Height = bar_height,
                Margin = new MarginPadding(2),
                Children = new Drawable[]
                {
                    judgementLinePool,
                    colourBars = new Container
                    {
                        Name = "colour axis",
                        X = chevron_size,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Width = judgement_line_width,
                        RelativeSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            colourBarsEarly = new Container
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.TopCentre,
                                Width = bar_width,
                                RelativeSizeAxes = Axes.Y,
                                Height = 0.5f,
                                Scale = new Vector2(1, -1),
                            },
                            colourBarsLate = new Container
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.TopCentre,
                                Width = bar_width,
                                RelativeSizeAxes = Axes.Y,
                                Height = 0.5f,
                            },
                            judgementsContainer = new Container
                            {
                                Name = "judgements",
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.Y,
                                Width = judgement_line_width,
                            },
                            labelEarly = new UprightAspectMaintainingContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.Centre,
                                Y = -10,
                            },
                            labelLate = new UprightAspectMaintainingContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.Centre,
                                Y = 10,
                            },
                        }
                    },
                    arrowContainer = new Container
                    {
                        Name = "average chevron",
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreRight,
                        Width = chevron_size,
                        X = chevron_size,
                        RelativeSizeAxes = Axes.Y,
                        Alpha = 0,
                        Scale = new Vector2(0, 1),
                        Child = arrow = new SpriteIcon
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.Y,
                            Y = 0.5f,
                            Icon = FontAwesome.Solid.ChevronRight,
                            Size = new Vector2(chevron_size),
                        }
                    },
                }
            };

            createColourBars(hitWindows);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            colourBars.Height = 0;
            colourBars.ResizeHeightTo(1, 800, Easing.OutQuint);

            CentreMarkerStyle.BindValueChanged(style => recreateCentreMarker(style.NewValue), true);
            LabelStyle.BindValueChanged(style => recreateLabels(style.NewValue), true);

            // delay the appearance animations for only the initial appearance.
            using (arrowContainer.BeginDelayedSequence(450))
            {
                ShowMovingAverage.BindValueChanged(visible =>
                {
                    arrowContainer.FadeTo(visible.NewValue ? 1 : 0, 250, Easing.OutQuint);
                    arrowContainer.ScaleTo(visible.NewValue ? new Vector2(1) : new Vector2(0, 1), 250, Easing.OutQuint);
                }, true);
            }
        }

        private void recreateCentreMarker(CentreMarkerStyles style)
        {
            if (centreMarkerDrawables != null)
            {
                foreach (var d in centreMarkerDrawables)
                {
                    d.ScaleTo(0, 500, Easing.OutQuint)
                     .FadeOut(500, Easing.OutQuint);

                    d.Expire();
                }

                centreMarkerDrawables = null;
            }

            switch (style)
            {
                case CentreMarkerStyles.None:
                    break;

                case CentreMarkerStyles.Circle:
                    centreMarkerDrawables = new Drawable[]
                    {
                        new Circle
                        {
                            Name = "middle marker behind",
                            Colour = GetColourForHitResult(hitWindows.Last().result),
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Depth = float.MaxValue,
                            Size = new Vector2(centre_marker_size),
                        },
                        new Circle
                        {
                            Name = "middle marker in front",
                            Colour = GetColourForHitResult(hitWindows.Last().result).Darken(0.3f),
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Depth = float.MinValue,
                            Size = new Vector2(centre_marker_size / 2f),
                        },
                    };
                    break;

                case CentreMarkerStyles.Line:
                    const float border_size = 1.5f;

                    centreMarkerDrawables = new Drawable[]
                    {
                        new Box
                        {
                            Name = "middle marker behind",
                            Colour = GetColourForHitResult(hitWindows.Last().result),
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Depth = float.MaxValue,
                            Size = new Vector2(judgement_line_width, centre_marker_size / 3f),
                        },
                        new Box
                        {
                            Name = "middle marker in front",
                            Colour = GetColourForHitResult(hitWindows.Last().result).Darken(0.3f),
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Depth = float.MinValue,
                            Size = new Vector2(judgement_line_width - border_size, centre_marker_size / 3f - border_size),
                        },
                    };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, null);
            }

            if (centreMarkerDrawables != null)
            {
                foreach (var d in centreMarkerDrawables)
                {
                    colourBars.Add(d);

                    d.FadeInFromZero(500, Easing.OutQuint)
                     .ScaleTo(0).ScaleTo(1, 1000, Easing.OutElasticHalf);
                }
            }
        }

        private void recreateLabels(LabelStyles style)
        {
            const float icon_size = 14;

            switch (style)
            {
                case LabelStyles.None:
                    labelEarly.Clear();
                    labelLate.Clear();
                    break;

                case LabelStyles.Icons:
                    labelEarly.Child = new SpriteIcon
                    {
                        Size = new Vector2(icon_size),
                        Icon = FontAwesome.Solid.ShippingFast,
                    };

                    labelLate.Child = new SpriteIcon
                    {
                        Size = new Vector2(icon_size),
                        Icon = FontAwesome.Solid.Bicycle,
                    };

                    break;

                case LabelStyles.Text:
                    labelEarly.Child = new OsuSpriteText
                    {
                        Text = "Early",
                        Font = OsuFont.Default.With(size: 10),
                        Height = 12,
                    };

                    labelLate.Child = new OsuSpriteText
                    {
                        Text = "Late",
                        Font = OsuFont.Default.With(size: 10),
                        Height = 12,
                    };

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, null);
            }

            labelEarly.FadeInFromZero(500);
            labelLate.FadeInFromZero(500);
        }

        private void createColourBars((HitResult result, double length)[] windows)
        {
            // max to avoid div-by-zero.
            maxHitWindow = Math.Max(1, windows.First().length);

            for (int i = 0; i < windows.Length; i++)
            {
                (var result, double length) = windows[i];

                float hitWindow = (float)(length / maxHitWindow);

                colourBarsEarly.Add(createColourBar(result, hitWindow, i == 0));
                colourBarsLate.Add(createColourBar(result, hitWindow, i == 0));
            }

            Drawable createColourBar(HitResult result, float height, bool requireGradient = false)
            {
                var colour = GetColourForHitResult(result);

                if (requireGradient)
                {
                    // the first bar needs gradient rendering.
                    const float gradient_start = 0.8f;

                    return new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = colour,
                                Height = height * gradient_start
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                RelativePositionAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(colour, colour.Opacity(0)),
                                Y = gradient_start,
                                Height = height * (1 - gradient_start)
                            },
                        }
                    };
                }

                return new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colour,
                    Height = height
                };
            }
        }

        protected override void OnNewJudgement(JudgementResult judgement)
        {
            const int arrow_move_duration = 800;

            if (!judgement.IsHit || judgement.HitObject.HitWindows?.WindowFor(HitResult.Miss) == 0)
                return;

            if (!judgement.Type.IsScorable() || judgement.Type.IsBonus())
                return;

            if (judgementsContainer.Count > max_concurrent_judgements)
            {
                const double quick_fade_time = 100;

                // check with a bit of lenience to avoid precision error in comparison.
                var old = judgementsContainer.FirstOrDefault(j => j.LifetimeEnd > Clock.CurrentTime + quick_fade_time * 1.1);

                if (old != null)
                {
                    old.ClearTransforms();
                    old.FadeOut(quick_fade_time).Expire();
                }
            }

            judgementLinePool.Get(drawableJudgement =>
            {
                drawableJudgement.Y = getRelativeJudgementPosition(judgement.TimeOffset);
                drawableJudgement.Colour = GetColourForHitResult(judgement.Type);

                judgementsContainer.Add(drawableJudgement);
            });

            arrow.MoveToY(
                getRelativeJudgementPosition(floatingAverage = floatingAverage * 0.9 + judgement.TimeOffset * 0.1)
                , arrow_move_duration, Easing.OutQuint);
        }

        private float getRelativeJudgementPosition(double value) => Math.Clamp((float)((value / maxHitWindow) + 1) / 2, 0, 1);

        internal class JudgementLine : PoolableDrawable
        {
            public readonly BindableNumber<float> JudgementLineThickness = new BindableFloat();

            public JudgementLine()
            {
                RelativeSizeAxes = Axes.X;
                RelativePositionAxes = Axes.Y;

                Blending = BlendingParameters.Additive;

                Origin = Anchor.Centre;
                Anchor = Anchor.TopCentre;

                InternalChild = new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                };
            }

            [Resolved]
            private BarHitErrorMeter barHitErrorMeter { get; set; }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                JudgementLineThickness.BindTo(barHitErrorMeter.JudgementLineThickness);
                JudgementLineThickness.BindValueChanged(thickness => Height = thickness.NewValue, true);
            }

            protected override void PrepareForUse()
            {
                base.PrepareForUse();

                const int judgement_fade_in_duration = 100;
                const int judgement_fade_out_duration = 5000;

                Alpha = 0;
                Width = 0;

                this
                    .FadeTo(0.6f, judgement_fade_in_duration, Easing.OutQuint)
                    .ResizeWidthTo(1, judgement_fade_in_duration, Easing.OutQuint)
                    .Then()
                    .FadeOut(judgement_fade_out_duration)
                    .ResizeWidthTo(0, judgement_fade_out_duration, Easing.InQuint)
                    .Expire();
            }
        }

        public override void Clear() => judgementsContainer.Clear();

        public enum CentreMarkerStyles
        {
            None,
            Circle,
            Line
        }

        public enum LabelStyles
        {
            None,
            Icons,
            Text
        }
    }
}
