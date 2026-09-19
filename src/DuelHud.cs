using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sparring
{
    /// <summary>
    /// Everything a duel needs to tell you, on screen, so it needs no commands to run.
    ///
    /// Drawn with IMGUI rather than Unity UI objects. Sparring has no Jotunn dependency, so there
    /// is no GUIManager canvas to borrow, and a hand-built canvas would be heavier than the few
    /// labels and bars it draws. IMGUI costs something every frame it runs, so the first check
    /// returns unless a duel or an invitation is on.
    ///
    /// Every panel is measured rather than placed. The font size is a setting, so fixed offsets
    /// inside a fixed box would clip or leave dead space; instead each row asks the style how tall
    /// it really is and the box is whatever the rows add up to.
    /// </summary>
    public static class DuelHud
    {
        private const float PadX = 12f;
        private const float PadY = 8f;
        private const float RowGap = 2f;
        private const float MinWidth = 220f;
        private const float MaxWidth = 560f;

        private static GUIStyle _label;
        private static GUIStyle _big;
        private static Texture2D _white;
        private static int _builtFor = -1;

        /// <summary>One line or bar of a panel, measured before anything is drawn.</summary>
        private struct Row
        {
            public string Text;
            public GUIStyle Style;
            public float Height;
            public float Fill;      // bars only: how full, 0..1
            public Color Color;     // bars only
            public bool IsBar;
        }

        private static readonly List<Row> _rows = new List<Row>();

        public static void Draw()
        {
            try
            {
                if (!Wanted()) return;

                Prepare();
                _rows.Clear();

                if (Duel.Active) BuildDuel();
                else if (Duel.HasInvite) BuildInvite();
                else if (Duel.HasOutgoing) BuildWaiting();
                else if (Scorecard.Showing) BuildCard();
                else if (Preview.Sparring) BuildRehearsal();
                else BuildStandIn();

                Flush();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Sparring HUD: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether to draw at all. Cheap and first, because OnGUI runs twice a frame and the answer
        /// is no for the whole of a normal session. Respects the game's own hide-HUD key and stays
        /// out of menus and cutscenes, where our overlay would be the only thing left on screen.
        /// </summary>
        private static bool Wanted()
        {
            if (!Duel.Active && !Duel.HasInvite && !Duel.HasOutgoing && !Scorecard.Showing
                && !Preview.Sparring && Preview.Panel == FakePanel.None) return false;

            var me = Player.m_localPlayer;
            if (me == null || me.InCutscene()) return false;

            if (Hud.IsUserHidden()) return false;
            if (Menu.IsVisible()) return false;

            return true;
        }

        // ---- what each panel is made of ----

        private static void BuildInvite() => Invite(Duel.InviteFrom, Mathf.CeilToInt((float)Duel.InviteSecondsLeft));

        private static void BuildWaiting() => Waiting(Duel.AwaitingFrom, Mathf.CeilToInt((float)Duel.InviteSecondsLeft));

        private static void BuildDuel()
        {
            if (Duel.CountingDown || Duel.ShowingStart)
            {
                Countdown(Duel.OpponentName, Mathf.CeilToInt((float)Duel.Terms.CountdownLeft));
                return;
            }

            // Name, health, and nothing else. The ring on the ground already shows where the edge is.
            var foe = Foe();
            if (foe == null)
            {
                Text(Duel.OpponentName);
                Text("<color=#cdd3d8>out of sight</color>");
            }
            else
            {
                Text(Duel.OpponentName);
                Health(Mathf.Clamp01(foe.GetHealth() / Mathf.Max(1f, foe.GetMaxHealth())));
            }

            if (Keys.YieldHeld) Bar(Keys.YieldProgress, new Color(0.85f, 0.62f, 0.25f), 6f);
        }

        /// <summary>
        /// The same panels with made-up contents, for looking at their size and placement without
        /// arranging a duel. Built by the very routines the real ones use, so what you judge here
        /// is what you will get.
        /// </summary>
        private static void BuildStandIn()
        {
            var seconds = Mathf.CeilToInt(Preview.Seconds);

            switch (Preview.Panel)
            {
                case FakePanel.Invite: Invite("Eyvind", seconds); break;
                case FakePanel.Waiting: Waiting("Eyvind", seconds); break;
                case FakePanel.Countdown: Countdown("Eyvind", Mathf.Max(0, seconds - 1)); break;
                case FakePanel.Active:
                    Text("Eyvind");
                    Health(Preview.FakeHealth);
                    break;
            }
        }

        /// <summary>
        /// What the duel cost, for a few seconds after it ends. Damage is rounded to whole numbers.
        /// </summary>
        private static void BuildCard()
        {
            switch (Scorecard.Outcome)
            {
                case DuelOutcome.Won:
                    Text($"<color=#9bc07a><b>Won</b></color> against {Scorecard.Against}", _big);
                    break;
                case DuelOutcome.Lost:
                    Text($"<color=#d08a72><b>Lost</b></color> to {Scorecard.Against}", _big);
                    break;
                default:
                    Text($"<b>{Scorecard.Against}</b>", _big);
                    break;
            }

            if (Scorecard.HeardBack)
            {
                Text($"dealt <b>{Mathf.RoundToInt(Scorecard.GaveTotal)}</b> over {Scorecard.GaveHits} hits" +
                     $"    <color=#cdd3d8>best {Mathf.RoundToInt(Scorecard.GaveBiggest)}</color>");
            }
            else
            {
                Text("<color=#cdd3d8>dealt — not reported</color>");
            }

            Text($"took <b>{Mathf.RoundToInt(Scorecard.TookTotal)}</b> over {Scorecard.TookHits} hits" +
                 $"    <color=#cdd3d8>worst {Mathf.RoundToInt(Scorecard.TookBiggest)}</color>");

            Text($"<color=#cdd3d8>{Clock(Scorecard.Seconds)}</color>");
        }

        /// <summary>
        /// Shown the whole time a rehearsal is running, since it changes how the world behaves
        /// around you and should not be left on by accident.
        /// </summary>
        private static void BuildRehearsal()
        {
            Text("<color=#d8c38a><b>Rehearsing a duel</b></color>", _big);
            Text("<color=#cdd3d8>/duel spar to stop</color>");
        }

        private static string Clock(float seconds)
        {
            var whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return whole < 60 ? $"{whole}s" : $"{whole / 60}m {whole % 60}s";
        }

        private static void Invite(string who, int seconds)
        {
            Text($"<b>{who}</b> challenges you", _big);
            Text($"[{KeyLabels.Of(Plugin.AcceptKey)}] Accept     [{KeyLabels.Of(Plugin.DeclineKey)}] Decline     {seconds}s");
        }

        private static void Waiting(string who, int seconds)
        {
            Text($"Waiting for <b>{who}</b>...  {seconds}s");
        }

        private static void Countdown(string who, int seconds)
        {
            Text(seconds > 0 ? $"<b>{seconds}</b>   {who}" : "<b>Fight!</b>", _big);
        }

        private static void Health(float fraction)
        {
            Bar(fraction, Color.Lerp(new Color(0.78f, 0.28f, 0.22f), new Color(0.55f, 0.72f, 0.4f), fraction), 9f);
        }

        // ---- layout ----

        /// <summary>
        /// Adds a line, asking the style how tall it actually needs to be at the widest the panel
        /// is allowed to get. Measuring against the maximum rather than the final width means a row
        /// can never end up shorter than the space its text was measured into.
        /// </summary>
        private static void Text(string text, GUIStyle style = null)
        {
            style = style ?? _label;
            var height = style.CalcHeight(new GUIContent(text), MaxWidth - PadX * 2f);
            _rows.Add(new Row { Text = text, Style = style, Height = height });
        }

        private static void Bar(float fill, Color color, float height)
        {
            _rows.Add(new Row { IsBar = true, Fill = Mathf.Clamp01(fill), Color = color, Height = height });
        }

        /// <summary>
        /// Works out the box from the rows, then draws it. Width follows the widest line so a long
        /// name widens the panel instead of being clipped by it, within bounds so it never becomes
        /// a banner across the screen.
        /// </summary>
        private static void Flush()
        {
            if (_rows.Count == 0) return;

            var width = MinWidth;
            var height = PadY * 2f;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                height += row.Height;
                if (i > 0) height += RowGap;

                if (row.IsBar) continue;
                var needed = row.Style.CalcSize(new GUIContent(row.Text)).x + PadX * 2f;
                if (needed > width) width = needed;
            }

            width = Mathf.Min(width, MaxWidth);

            var box = new Rect(
                Mathf.Round((Screen.width - width) * 0.5f),
                Mathf.Round(Screen.height * Mathf.Clamp01(Plugin.HudY)),
                Mathf.Round(width),
                Mathf.Round(height));

            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(box, White());
            GUI.color = Color.white;

            var y = box.y + PadY;
            foreach (var row in _rows)
            {
                if (row.IsBar)
                {
                    var bar = new Rect(box.x + PadX, y + 1f, box.width - PadX * 2f, row.Height - 2f);
                    GUI.color = new Color(0f, 0f, 0f, 0.7f);
                    GUI.DrawTexture(bar, White());

                    GUI.color = row.Color;
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * row.Fill, bar.height), White());
                    GUI.color = Color.white;
                }
                else
                {
                    var rect = new Rect(box.x + PadX, y, box.width - PadX * 2f, row.Height);

                    // Drawn twice, the first offset and blackened. GUI.color multiplies whatever
                    // the glyph would have been, so a black tint flattens even the rich-text
                    // colours in these strings to a shadow — which is what keeps a grey "47s"
                    // readable over snow, and does not depend on the panel behind it being dark
                    // enough on its own.
                    GUI.color = new Color(0f, 0f, 0f, 0.8f);
                    GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), row.Text, row.Style);

                    GUI.color = Color.white;
                    GUI.Label(rect, row.Text, row.Style);
                }

                y += row.Height + RowGap;
            }

            _rows.Clear();
        }

        private static Character Foe()
        {
            var id = Duel.Opponent;
            return id.IsNone() ? null : Lease.FindPlayer(id);
        }

        /// <summary>
        /// Builds the styles, and rebuilds them when the text size setting changes, so a new size
        /// takes effect without a restart.
        /// </summary>
        private static void Prepare()
        {
            if (_label != null && _builtFor == Plugin.HudFontSize) return;

            _builtFor = Plugin.HudFontSize;

            _label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                fontSize = Plugin.HudFontSize,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _label.normal.textColor = Color.white;

            _big = new GUIStyle(_label) { fontSize = Plugin.HudFontSize + 3, fontStyle = FontStyle.Bold };
        }

        private static Texture2D White()
        {
            if (_white != null) return _white;

            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _white.hideFlags = HideFlags.HideAndDontSave;
            return _white;
        }
    }
}
