using UnityEngine;

namespace Sparring
{
    /// <summary>Which panel a preview is standing in for.</summary>
    public enum FakePanel
    {
        None,
        Invite,
        Waiting,
        Countdown,
        Active,
    }

    /// <summary>
    /// Ways to see a duel's visuals without a duel.
    ///
    /// Most of what a duel puts on screen (the ring, the panels, the stagger on yielding) is drawn
    /// and played on one machine. Only the handshake and the announcement need two, so the rest
    /// can be shown alone.
    /// </summary>
    public static class Preview
    {
        private const float PanelSeconds = 4f;

        private const float MessageGap = 1.6f;

        private static bool _ring;
        private static Vector3 _ringCenter;
        private static float _ringRadius;
        private static FakePanel _panel;
        private static float _panelUntil;
        private static float _fakeHealth = 0.62f;

        private static string[] _messages;
        private static int _messageAt;
        private static float _messageNext;

        private static bool _sparring;

        /// <summary>
        /// A duel with nobody in it, for testing the parts that need no opponent.
        ///
        /// Several of a duel's rules only affect the person under them (blows sparing buildings,
        /// creatures losing interest, skill gain), and each is gated on a corroborated lease, which
        /// needs a second player. This enables just those, alone.
        ///
        /// It grants nothing that reaches another player: no PvP bypass, no forced enmity, no
        /// protection from anyone's blows. Everything it enables is self-limiting or a handicap.
        /// </summary>
        public static bool Sparring => _sparring;

        /// <summary>Whether this character is the one rehearsing. Only ever the local player.</summary>
        public static bool Rehearsing(Character c)
        {
            return _sparring && c != null && c == Player.m_localPlayer;
        }

        public static void ToggleSparring()
        {
            if (Duel.Active) { Duel.Say("Not while you are dueling."); return; }

            _sparring = !_sparring;
            Duel.Say(_sparring
                ? "Rehearsing a duel. Buildings and skills behave as if you were in one."
                : "Rehearsal over.");
        }

        /// <summary>Called when a real duel begins, so the pretend one cannot be confused with it.</summary>
        public static void StopSparring()
        {
            if (!_sparring) return;
            _sparring = false;
            Duel.Say("Rehearsal ended — you are in a real duel now.");
        }

        public static bool RingUp => _ring;
        public static FakePanel Panel => _panel;
        public static float FakeHealth => _fakeHealth;

        /// <summary>Seconds left on the stand-in panel, so it can count down like the real one.</summary>
        public static float Seconds => Mathf.Max(0f, _panelUntil - Time.realtimeSinceStartup);

        public static void Tick()
        {
            TickRing();
            TickMessages();
            TickPanels();
        }

        /// <summary>
        /// A preview ring behaves like a real arena: step outside it and it calls itself off with
        /// the very line a duel uses. That is the only way to rehearse the boundary alone — the
        /// real check needs a duel, and a duel needs two people — and it exercises the same flat
        /// distance-from-centre maths, so what you feel here is what will end a fight.
        /// </summary>
        private static void TickRing()
        {
            if (!_ring) return;

            var me = Player.m_localPlayer;
            if (me == null) { ClearRing(); return; }

            var here = me.transform.position;
            var flat = new Vector2(here.x - _ringCenter.x, here.z - _ringCenter.z).magnitude;
            if (flat <= _ringRadius) return;

            ClearRing();
            Duel.Say(Messages.OutOfRing);
        }

        private static void TickMessages()
        {
            if (_messages == null) return;

            if (Time.realtimeSinceStartup < _messageNext) return;

            if (_messageAt >= _messages.Length)
            {
                _messages = null;
                return;
            }

            Duel.Say(_messages[_messageAt]);
            _messageAt++;
            _messageNext = Time.realtimeSinceStartup + MessageGap;
        }

        private static void TickPanels()
        {
            if (_panel == FakePanel.None) return;

            if (Time.realtimeSinceStartup < _panelUntil) return;

            // Walks the states in the order you meet them, then stops, so one command shows the
            // whole sequence at the size it will really be.
            switch (_panel)
            {
                case FakePanel.Invite: Begin(FakePanel.Waiting); break;
                case FakePanel.Waiting: Begin(FakePanel.Countdown); break;
                case FakePanel.Countdown: Begin(FakePanel.Active); break;
                default: _panel = FakePanel.None; break;
            }
        }

        /// <summary>
        /// Puts a ring where you stand, or takes it away, to check a spot before challenging
        /// someone on it.
        /// </summary>
        public static void ToggleRing(float radius)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            if (_ring)
            {
                _ring = false;
                ArenaRing.HidePreview();
                Duel.Say("Ring preview off.");
                return;
            }

            // A live duel owns the ring; a preview must not pull it out from under one.
            if (Duel.Active) { Duel.Say("Not while you are dueling."); return; }

            _ring = true;
            _ringCenter = me.transform.position;
            _ringRadius = radius;
            ArenaRing.ShowPreview(new Lease.Terms { Center = _ringCenter, Radius = radius });
            Duel.Say($"Ring preview: {Mathf.RoundToInt(radius)}m. Step out of it to see a duel end.");
        }

        public static void ClearRing()
        {
            if (!_ring) return;
            _ring = false;
            _ringRadius = 0f;
            ArenaRing.HidePreview();
        }

        /// <summary>
        /// Prints the end-of-duel line into your own chat, exactly as receiving one would.
        ///
        /// Tests the half that can be tested alone: composing the line, putting it in the buffer,
        /// and getting the window to show itself. It says nothing about whether the message reaches
        /// anyone — that is the routing, and routing needs somebody to route to.
        /// </summary>
        public static void Announce()
        {
            var me = Player.m_localPlayer;
            if (me == null) return;

            Announcer.Show("Eyvind", me.GetPlayerName(), EndReason.Yielded);
        }

        public static void ShowPanels()
        {
            Begin(FakePanel.Invite);
            Duel.Say("Panel preview: invite, waiting, countdown, duel.");
        }

        /// <summary>
        /// Walks every centre-screen line the mod can say, one every second and a half. These are
        /// the refusals and endings, each of which normally needs its own awkward situation to
        /// provoke — challenging yourself, being in combat, walking away mid-offer. Read straight
        /// from <see cref="Messages"/>, so this shows the real wording rather than a copy that
        /// could quietly fall behind it.
        /// </summary>
        public static void ShowMessages()
        {
            _messages = Messages.All("Eyvind");
            _messageAt = 0;
            _messageNext = 0f;
            Duel.Say($"{_messages.Length} messages, one every {MessageGap:0.0}s.");
            _messageNext = Time.realtimeSinceStartup + MessageGap;
        }

        /// <summary>
        /// Runs what a yielding fighter goes through, on you, with no duel involved: the debuffs
        /// come off, the health comes back, the stagger plays.
        /// </summary>
        public static void Yield()
        {
            var me = Player.m_localPlayer;
            if (me == null) return;
            if (Duel.Active) { Duel.Say("Not while you are dueling."); return; }

            Recovery.Settle(me);
            Recovery.Restore(me, Plugin.YieldHealth);
            me.Stagger(-me.transform.forward);
            Scorecard.Rehearse("Eyvind", DuelOutcome.Lost);
        }

        /// <summary>
        /// The winning end, on you, with nobody beaten: the burst pulses over your head and the
        /// emote plays. Useful for trying out WinnerEmote.
        /// </summary>
        public static void Win()
        {
            var me = Player.m_localPlayer;
            if (me == null) return;
            if (Duel.Active) { Duel.Say("Not while you are dueling."); return; }

            Recovery.Settle(me);
            Recovery.Restore(me, Plugin.VictoryHealth);
            Victory.Celebrate(me.GetZDOID());
            Victory.Emote(me);
            // No message: the card already says which way it went.
            Scorecard.Rehearse("Eyvind", DuelOutcome.Won);
        }

        /// <summary>
        /// Lists prefab names matching some text, into the console, so a centre marker can be
        /// picked by looking rather than guessing. Capped, since the full list runs to thousands.
        /// </summary>
        public static void ListPrefabs(string filter)
        {
            var scene = ZNetScene.instance;
            if (scene == null) { Duel.Say("Not in a world yet."); return; }

            if (string.IsNullOrEmpty(filter)) { Duel.Say("Add a search term: /duel prefabs torch"); return; }

            var names = scene.GetPrefabNames();
            var shown = 0;

            foreach (var name in names)
            {
                if (name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                Plugin.Log.LogInfo($"prefab: {name}");
                Console.instance?.AddString(name);
                if (++shown >= 40) break;
            }

            var summary = shown == 0
                ? $"Nothing matching \"{filter}\"."
                : $"{shown} matching \"{filter}\" — in the console (F5) and the log.";
            Duel.Say(summary);
        }

        private static void Begin(FakePanel panel)
        {
            _panel = panel;
            _panelUntil = Time.realtimeSinceStartup + PanelSeconds;
            if (panel == FakePanel.Active) _fakeHealth = 0.62f;
        }

    }
}
