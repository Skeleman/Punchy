using System.Collections;
using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Entities;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// "-talktest N exchanges" (P-42 row 2): walks the shipped dialogue's reply exchanges through the window, as a
    /// player would. It meets folk until it has seen a guard's, a hermit's and an innkeeper's work line with replies
    /// (or runs out of tries): the guard's "Need a hand?" quest turned down at one guard and taken by a reply at the next
    /// (one already held from the save shows a note instead of replies), the hermit's bow,
    /// and the innkeeper's reply with no follow-up, which goes back to the topics.
    /// </summary>
    public partial class FolkTalk
    {
        bool testExchanges;

        IEnumerator Exchanges(HashSet<uint> tried)
        {
            var bad = new List<string>();
            void Check(bool ok, string what)
            {
                Debug.Log($"[talk] exchanges: {(ok ? "ok" : "BAD")}: {what}");
                if (!ok)
                    bad.Add(what);
            }
            string Listed() => string.Join(" | ", choices.ConvertAll(c => c.Label));
            int Reply(string say) => choices.FindIndex(c => c.Label == say);
            bool AtWork()
            {
                var work = choices.FindIndex(c => c.Id == "work");
                if (replying || work < 0)
                    return false;
                Choose(work);
                return replying;
            }
            bool guard = false, quest = false, declined = false, heldShot = false, hermit = false, inn = false;
            int met = 0;
            for (int n = 0; n < 80 && !(quest && hermit && inn); n++)
            {
                var e = NextFolk(tried, out var s);
                for (int hop = 0; e == Entity.Null && hop < 8; hop++)
                {
                    nextDoor += 20;
                    Hop(nextDoor);
                    yield return new WaitForSecondsRealtime(6f);
                    e = NextFolk(tried, out s);
                }
                if (e == Entity.Null)
                {
                    TryPlayer(out var at);
                    Debug.Log($"[talk] exchanges: no folk to meet near door {nextDoor} at {at.Position}");
                    break;
                }
                tried.Add(s.Individual);
                StandBefore(s);
                yield return new WaitForSecondsRealtime(0.3f);
                var ok = false;
                yield return Press(e, (o, d) => ok = o && talking);
                if (!ok)
                {
                    Debug.Log($"[talk] exchanges: no talk with #{s.Individual:X8} (aimed {target == e}, window {talking})");
                    continue;
                }
                met++;
                if (!AtWork())
                {
                    End("test");
                    yield return new WaitForSecondsRealtime(0.4f);
                    continue;
                }
                var list = Listed();
                Debug.Log($"[talk] exchanges: {title}: \"{shown.Text}\" lists {list}");
                if (!quest && Reply("Need a hand?") >= 0)
                {
                    guard = true;
                    if (!declined && !heldShot)
                        yield return Shot("guard");
                    Choose(Reply("Need a hand?"));
                    if (offering && shown.Offer.Kind == OfferKind.Quest)
                    {
                        var id = shown.Offer.Id;
                        Check(Reply("I'll do it.") >= 0 && Reply("Not right now.") >= 0, $"the guard's quest lists its replies: {Listed()}");
                        // (The first guard's quest is turned down, the next one's taken: asking work again draws a new line.)
                        if (!declined)
                        {
                            yield return Shot("guard-quest");
                            Choose(Reply("Not right now."));
                            Check(!replying && !Items.Items.HasFolkQuest(id) && choices.Exists(c => c.Id == "work") && body.Contains("You: Not right now."),
                                  $"turned down: back to the topics, no quest ({body.Replace('\n', ' ')})");
                            declined = true;
                        }
                        else
                        {
                            Choose(Reply("I'll do it."));
                            Check(Items.Items.HasFolkQuest(id) && body.Contains("Added to your quests") && body.Contains("You: I'll do it."),
                                  $"taken by a reply: quest {id} held ({body.Replace('\n', ' ')})");
                            Check(shown.Topic == "guard_thanks" && shown.Text != null, $"the guard thanks ({shown.Topic}: \"{shown.Text}\", {Listed()})");
                            yield return Shot("guard-taken");
                            quest = true;
                        }
                    }
                    else if (shown.Offer.Kind == OfferKind.Quest)
                    {
                        Check(Items.Items.HasFolkQuest(shown.Offer.Id) && body.Contains("(You already have this quest.)") && !replying,
                              $"a quest already held: no replies, a note ({body.Replace('\n', ' ')})");
                        if (!heldShot)
                            yield return Shot("guard-held");
                        heldShot = true;
                    }
                    else
                        Debug.Log($"[talk] exchanges: the guard offers no quest: \"{shown.Text}\" lists {Listed()}");
                }
                else if (!hermit && Reply("Why did you leave?") >= 0)
                {
                    hermit = true;
                    Choose(Reply("Why did you leave?"));
                    Check(replying && Reply("Sorry. I'll be quiet.") >= 0, $"the hermit's why lists {Listed()}");
                    yield return Shot("hermit");
                    Choose(Reply("Sorry. I'll be quiet."));
                    Check(shown.Action == IdleAction.Bow && !replying && choices.Exists(c => c.Id == "work"), $"the bow: back to the topics ({shown.Action})");
                }
                else if (!inn && Reply("A bed for the night?") >= 0)
                {
                    inn = true;
                    Choose(Reply("A bed for the night?"));
                    Check(replying && Reply("Maybe another night.") >= 0, $"the room lists {Listed()}");
                    yield return Shot("inn");
                    Choose(Reply("Maybe another night."));
                    Check(!replying && choices.Exists(c => c.Id == "work") && body.Contains("Second door") && body.Contains("You: Maybe another night."),
                          $"a reply with no follow-up: back to the topics ({body.Replace('\n', ' ')})");
                }
                End("test");
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Check(guard || hermit || inn, "an exchange was found");
            // (A save that already holds the guards' quests gets the note instead: that walks the same exchange.)
            Check(!guard || quest || heldShot, "the guard's quest was taken by a reply, or shown as already held");
            Debug.Log($"[talk] exchanges: met {met}, guard {guard}, quest {quest}, hermit {hermit}, innkeeper {inn}");
            Debug.Log($"[talk] exchanges => {(bad.Count == 0 ? "PASS" : "FAIL (" + string.Join("; ", bad) + ")")}");
        }
    }
}
