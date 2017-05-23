// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Chat;

namespace osu.Game.Overlays.Chat
{
    public class ChannelSelectionOverlay : OverlayContainer
    {
        public static readonly float WIDTH_PADDING = 170;

        private readonly Box bg;
        private readonly Triangles triangles;
        private readonly Box headerBg;
        private readonly SearchTextBox search;
        private readonly SearchContainer<ChannelSection> sectionsFlow;

        public Action<Channel> OnRequestJoin;

        public IEnumerable<ChannelSection> Sections
        {
            set
            {
                sectionsFlow.Children = value;

                foreach (ChannelSection s in sectionsFlow.Children)
                {
                    foreach (ChannelListItem c in s.ChannelFlow.Children)
                    {
                        c.OnRequestJoin = channel => { OnRequestJoin?.Invoke(channel); };
                    }
                }
            }
        }

        public ChannelSelectionOverlay()
        {
            RelativeSizeAxes = Axes.X;

            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        bg = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                        triangles = new Triangles
                        {
                            RelativeSizeAxes = Axes.Both,
                            TriangleScale = 5,
                        },
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = 85 },
                    Children = new[]
                    {
                        new ScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollDraggerVisible = false,
                            Children = new[]
                            {
                                sectionsFlow = new SearchContainer<ChannelSection>
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    LayoutDuration = 200,
                                    LayoutEasing = EasingTypes.OutQuint,
                                    Spacing = new Vector2(0f, 20f),
                                    Padding = new MarginPadding { Top = 20, Bottom = 20, Left = WIDTH_PADDING, Right = WIDTH_PADDING },
                                },
                            },
                        },
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        headerBg = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0f, 10f),
                            Padding = new MarginPadding { Top = 10f, Bottom = 10f, Left = WIDTH_PADDING, Right = WIDTH_PADDING },
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = @"Chat Channels",
                                    TextSize = 20,
                                    Shadow = false,
                                },
                                search = new HeaderSearchTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    PlaceholderText = @"Search",
                                    Exit = Hide,
                                },
                            },
                        },
                    },
                },
            };

            search.Current.ValueChanged += newValue => sectionsFlow.SearchTerm = newValue;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            bg.Colour = colours.Gray3;
            triangles.ColourDark = colours.Gray3;
            triangles.ColourLight = OsuColour.FromHex(@"353535");

            headerBg.Colour = colours.Gray2.Opacity(0.75f);
        }

        protected override void PopIn()
        {
            search.HoldFocus = true;
            Schedule(() => search.TriggerFocus());

            FadeIn(100, EasingTypes.OutQuint);
            MoveToY(0, 800, EasingTypes.OutQuint);
        }

        protected override void PopOut()
        {
            search.HoldFocus = false;

            FadeOut(500, EasingTypes.InQuint);
            MoveToY(DrawHeight, 500, EasingTypes.In);
        }

        private class HeaderSearchTextBox : SearchTextBox
        {
            protected override Color4 BackgroundFocused => Color4.Black.Opacity(0.2f);
            protected override Color4 BackgroundUnfocused => Color4.Black.Opacity(0.2f);
        }
    }
}
