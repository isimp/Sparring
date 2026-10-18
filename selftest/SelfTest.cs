using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;
using Result = Sparring.SelfTestReport.Result;

namespace Sparring
{
    /// <summary>
    /// The in-game self-test: <c>/duel selftest</c> checks Sparring against the running game and
    /// reports how it went, then writes the report to the console and <c>Sparring-selftest.txt</c>
    /// beside the BepInEx log.
    ///
    /// Most of it reads: whether every hook went in, whether the game still has what Sparring
    /// reads, whether the sounds and the ring resolve, and how the opponent rules judge a hit. The
    /// last part fights: a duel against yourself, ending three times in a blow, a burn and a
    /// poison that each take you to zero through the game's own death check. A broken yield there
    /// is a real death, which is why this lives in a plugin that no release carries.
    ///
    /// It also answers the practice words, so a duel against yourself can be staged by hand:
    /// <c>/duel practice</c>, then <c>/duel hit</c>, <c>burn</c> or <c>poison</c> with an amount.
    /// </summary>
    internal sealed partial class SelfTest : SelfTestHost.IRunner
    {
        private const string FileName = "Sparring-selftest.txt";

        private readonly MonoBehaviour _host;
        private bool _running;

        public SelfTest(MonoBehaviour host)
        {
            _host = host;
        }

        public bool Answer(string word, Terminal.ConsoleEventArgs args)
        {
            switch (word)
            {
                case "selftest":
                    Begin();
                    return true;

                case "practice":
                    Duel.Practice();
                    return true;

                case "hit":
                    Duel.SelfHit(Amount(args, 10f));
                    return true;

                // Fire and poison do not land with the blow that carried them, so they need their
                // own way in: these hand the damage to the effects that tick it out.
                case "burn":
                    Duel.SelfBurn(Amount(args, 60f));
                    return true;

                case "poison":
                    Duel.SelfPoison(Amount(args, 60f));
                    return true;

                default:
                    return false;
            }
        }

        private void Begin()
        {
            if (_running) { Duel.Say("The self-test is already running."); return; }
            if (Player.m_localPlayer == null) { Duel.Say("The self-test needs a world and a character."); return; }

            _host.StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _running = true;
            var checks = new List<SelfTestReport.Check>();
            var finished = false;

            Duel.Say("Sparring self-test: stand still somewhere safe. It takes about half a minute and knocks you down three times.");

            try
            {
                Guard(checks, "Hooks", Hooks);
                Guard(checks, "Game", Members);
                Guard(checks, "Sounds", Sounds);
                Guard(checks, "Ring", Ring);
                Guard(checks, "Rules", Rules);

                // Run step by step here rather than as a coroutine of its own, so a fault inside
                // it still reaches the report instead of stopping the run without a word.
                var duel = Lifecycle(checks);
                while (true)
                {
                    object step;
                    try
                    {
                        if (!duel.MoveNext()) break;
                        step = duel.Current;
                    }
                    catch (Exception ex)
                    {
                        // The failure is the check's result, told in the report as a fail.
                        checks.Add(new SelfTestReport.Check("Duel", "the duel against yourself ran to the end", Result.Fail, Fault(ex)));
                        break;
                    }
                    yield return step;
                }

                finished = true;
            }
            finally
            {
                if (!finished)
                {
                    checks.Add(new SelfTestReport.Check("Self-test", "ran to the end", Result.Fail, "it stopped early; the log has the error"));
                }

                CleanUp();
                Report(checks);
                _running = false;
            }
        }

        /// <summary>Runs one area's checks, so an area that throws is a failure in the report rather than the end of the run.</summary>
        private static void Guard(List<SelfTestReport.Check> checks, string area, Action<List<SelfTestReport.Check>> run)
        {
            try
            {
                run(checks);
            }
            catch (Exception ex)
            {
                // The failure is the area's result, told in the report as a fail.
                checks.Add(new SelfTestReport.Check(area, "its checks ran", Result.Fail, Fault(ex)));
            }
        }

        /// <summary>Leaves no practice duel behind, whatever stopped the run.</summary>
        private static void CleanUp()
        {
            try
            {
                if (Duel.Active && Duel.Practising) Duel.EndLocal(EndReason.Withdrew, notify: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring self-test could not end its practice duel: {ex.Message}");
            }
        }

        private static void Report(List<SelfTestReport.Check> checks)
        {
            var lines = SelfTestReport.Lines(checks);
            var console = Console.instance;
            foreach (var line in lines)
            {
                Plugin.Log.LogInfo(line);
                if (console != null) console.AddString(line);
            }

            try
            {
                File.WriteAllText(Path.Combine(Paths.BepInExRootPath, FileName), string.Join("\r\n", lines) + "\r\n");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Sparring self-test could not write {FileName}: {ex.Message}");
            }

            Duel.Say(lines[0] + ". The console (F5) and " + FileName + " have every check.");
        }

        private static void Add(List<SelfTestReport.Check> checks, string area, string what, bool passed, string detail, Result otherwise = Result.Fail)
        {
            checks.Add(new SelfTestReport.Check(area, what, passed ? Result.Pass : otherwise, detail));
        }

        private static float Amount(Terminal.ConsoleEventArgs args, float fallback)
        {
            return args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var given)
                ? given
                : fallback;
        }

        private static string Fault(Exception ex) => ex.GetType().Name + ": " + ex.Message;

        private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
