﻿using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JecsTools
{
    // TODO: This hasn't worked properly since RW B19.
    // It's based off the B18 version of FactionDialogMaker, which has undergone significant changes since.
    // For example, the QuestPeaceTalks IncidentDef no longer exists:
    // It was renamed to Quest_PeaceTalks in RW B19, then replaced with an OpportunitySite_PeaceTalks QuestScriptDef in RW 1.1.
    // This needs to be rebased off the latest version of FactionDialogMaker (or reworked into Harmony patches if possible).
    [Obsolete("Hasn't worked properly since RW B19")]
    public static class JecsToolsFactionDialogMaker
    {
        public static DiaNode FactionDialogFor(Pawn negotiator, Faction faction)
        {
            var map = negotiator.Map;
            JecsToolsFactionDialogMaker.negotiator = negotiator;
            JecsToolsFactionDialogMaker.faction = faction;
            var text = (faction.leader != null) ? faction.leader.Name.ToStringFull : faction.Name;
            var factionSettings = faction.def?.GetFactionSettings();
            var greetingHostileKey = factionSettings?.greetingHostileKey ?? "FactionGreetingHostile";
            var greetingWaryKey = factionSettings?.greetingWaryKey ?? "FactionGreetingWary";
            var greetingWarmKey = factionSettings?.greetingWarmKey ?? "FactionGreetingWarm";
            var waryMinimum = factionSettings?.waryMinimumRelations ?? MinRelationsToCommunicate;
            var warmMinimum = factionSettings?.warmMinimumRelations ?? MinRelationsFriendly;

            var greetingHostile = greetingHostileKey.Translate(text);
            var greetingWary = greetingWaryKey.Translate(text, negotiator.LabelShort);
            var greetingWarm = greetingWarmKey.Translate(text, negotiator.LabelShort);

            if (faction.PlayerGoodwill < waryMinimum)
            {
                root = new DiaNode(greetingHostile);
                if (!SettlementUtility.IsPlayerAttackingAnySettlementOf(faction) && negotiator.Spawned && negotiator.Map.IsPlayerHome)
                {
                    root.options.Add(PeaceTalksOption(faction));
                }
            }
            else
            {
                if (faction.PlayerGoodwill < warmMinimum)
                {
                    greetingWary = greetingWary.AdjustedFor(negotiator);
                    root = new DiaNode(greetingWary);
                    if (!SettlementUtility.IsPlayerAttackingAnySettlementOf(faction))
                    {
                        root.options.Add(OfferGiftOption(negotiator.Map));
                    }
                    if (!faction.HostileTo(Faction.OfPlayer) && negotiator.Spawned && negotiator.Map.IsPlayerHome)
                    {
                        root.options.Add(RequestTraderOption(map, TradeRequestCost_Wary));
                    }
                }
                else
                {
                    root = new DiaNode(greetingWarm);
                    if (!SettlementUtility.IsPlayerAttackingAnySettlementOf(faction))
                    {
                        root.options.Add(OfferGiftOption(negotiator.Map));
                    }
                    if (!faction.HostileTo(Faction.OfPlayer) && negotiator.Spawned && negotiator.Map.IsPlayerHome)
                    {
                        root.options.Add(RequestTraderOption(map, TradeRequestCost_Warm));
                        root.options.Add(RequestMilitaryAidOption(map));
                    }
                }
            }
            if (Prefs.DevMode)
            {
                foreach (var item in DebugOptions())
                {
                    root.options.Add(item);
                }
            }
            var diaOption = new DiaOption("(" + "Disconnect".Translate() + ")")
            {
                resolveTree = true,
            };
            root.options.Add(diaOption);
            return root;
        }

        private static IEnumerable<DiaOption> DebugOptions()
        {
            var opt = new DiaOption("(Debug) Goodwill +10")
            {
                action = () => faction.TryAffectGoodwillWith(Faction.OfPlayer, 10, false, true),
                linkLateBind = () => FactionDialogFor(negotiator, faction),
            };
            yield return opt;
            var opt2 = new DiaOption("(Debug) Goodwill -10")
            {
                action = () => faction.TryAffectGoodwillWith(Faction.OfPlayer, -10, false, true),
                linkLateBind = () => FactionDialogFor(negotiator, faction),
            };
            yield return opt2;
            var opt3 = new DiaOption("(Debug) Potatoes")
            {
                action = () => Messages.Message("Boil 'em, mash 'em, stick em in a stew.", MessageTypeDefOf.PositiveEvent),
                linkLateBind = () => FactionDialogFor(negotiator, faction),
            };
            yield return opt3;
        }

        private static int AmountSendableSilver(Map map)
        {
            return (from t in TradeUtility.AllLaunchableThingsForTrade(map)
                    where t.def == ThingDefOf.Silver
                    select t).Sum((Thing t) => t.stackCount);
        }

        private static DiaOption PeaceTalksOption(Faction faction)
        {
            var def = IncidentDef.Named("QuestPeaceTalks"); // XXX: this doesn't exist anymore - see comment on class
            if (PeaceTalksExist(faction))
            {
                var diaOption = new DiaOption(def.letterLabel);
                diaOption.Disable("InProgress".Translate());
                return diaOption;
            }

            var diaOption2 = new DiaOption(def.letterLabel)
            {
                action = () =>
                {
                    PlaySoundFor(faction);
                    if (!TryStartPeaceTalks(faction))
                        Log.Error("Peace talks event failed to start. This should never happen.");
                },
            };
            var text = string.Format(def.letterText.AdjustedFor(faction.leader), faction.def.leaderTitle, faction.Name, 15)
                .CapitalizeFirst();
            diaOption2.link = new DiaNode(text)
            {
                options =
                {
                    OKToRoot(),
                },
            };
            return diaOption2;
        }

        private static DiaOption OfferGiftOption(Map map)
        {
            if (AmountSendableSilver(map) < GiftSilverAmount)
            {
                var diaOption = new DiaOption("OfferGift".Translate());
                diaOption.Disable("NeedSilverLaunchable".Translate(GiftSilverAmount));
                return diaOption;
            }
            var goodwillDelta = GiftSilverGoodwillChange * negotiator.GetStatValue(StatDefOf.NegotiationAbility, true);
            var diaOption2 = new DiaOption("OfferGift".Translate() + " (" + "SilverForGoodwill".Translate(GiftSilverAmount, goodwillDelta.ToString("#####0")) + ")")
            {
                action = () =>
                {
                    TradeUtility.LaunchThingsOfType(ThingDefOf.Silver, GiftSilverAmount, map, null);
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, (int)goodwillDelta);
                    PlaySoundFor(faction);
                },
            };
            var text = "SilverGiftSent".Translate(faction.leader.LabelIndefinite(), Mathf.RoundToInt(goodwillDelta)).CapitalizeFirst();
            diaOption2.link = new DiaNode(text)
            {
                options =
                {
                    OKToRoot(),
                },
            };
            return diaOption2;
        }

        private static DiaOption RequestTraderOption(Map map, int silverCost)
        {
            var text = "RequestTrader".Translate(silverCost.ToString());
            if (AmountSendableSilver(map) < silverCost)
            {
                var diaOption = new DiaOption(text);
                diaOption.Disable("NeedSilverLaunchable".Translate(silverCost));
                return diaOption;
            }
            if (!faction.def.allowedArrivalTemperatureRange.ExpandedBy(-4f).Includes(map.mapTemperature.SeasonalTemp))
            {
                var diaOption2 = new DiaOption(text);
                diaOption2.Disable("BadTemperature".Translate());
                return diaOption2;
            }
            var num = faction.lastTraderRequestTick + 240000 - Find.TickManager.TicksGame;
            if (num > 0)
            {
                var diaOption3 = new DiaOption(text);
                diaOption3.Disable("WaitTime".Translate(num.ToStringTicksToPeriod()));
                return diaOption3;
            }
            var diaOption4 = new DiaOption(text);
            var diaNode = new DiaNode("TraderSent".Translate(faction.leader.LabelIndefinite()).CapitalizeFirst());
            diaNode.options.Add(OKToRoot());
            var diaNode2 = new DiaNode("ChooseTraderKind".Translate(faction.leader.LabelIndefinite()));
            foreach (var localTk2 in faction.def.caravanTraderKinds)
            {
                var localTk = localTk2;
                var diaOption5 = new DiaOption(localTk.LabelCap)
                {
                    action = () =>
                    {
                        var incidentParms = new IncidentParms
                        {
                            target = map,
                            faction = faction,
                            traderKind = localTk,
                            forced = true,
                        };
                        Find.Storyteller.incidentQueue.Add(IncidentDefOf.TraderCaravanArrival, Find.TickManager.TicksGame + 120000, incidentParms);
                        faction.lastTraderRequestTick = Find.TickManager.TicksGame;
                        TradeUtility.LaunchThingsOfType(ThingDefOf.Silver, silverCost, map, null);
                        PlaySoundFor(faction);
                    },
                    link = diaNode,
                };
                diaNode2.options.Add(diaOption5);
            }
            var diaOption6 = new DiaOption("GoBack".Translate())
            {
                linkLateBind = ResetToRoot(),
            };
            diaNode2.options.Add(diaOption6);
            diaOption4.link = diaNode2;
            return diaOption4;
        }

        private static DiaOption RequestMilitaryAidOption(Map map)
        {
            var text = "RequestMilitaryAid".Translate(MilitaryAidRelsChange);
            if (!faction.def.allowedArrivalTemperatureRange.ExpandedBy(-4f).Includes(map.mapTemperature.SeasonalTemp))
            {
                var diaOption = new DiaOption(text);
                diaOption.Disable("BadTemperature".Translate());
                return diaOption;
            }
            var diaOption2 = new DiaOption(text);
            if (map.attackTargetsCache.TargetsHostileToColony.Any(GenHostility.IsActiveThreatToPlayer))
            {
                if (!map.attackTargetsCache.TargetsHostileToColony
                    .Any(p => ((Thing)p).Faction != null && ((Thing)p).Faction.HostileTo(faction)))
                {
                    var source = (from pa in map.attackTargetsCache.TargetsHostileToColony
                                  where GenHostility.IsActiveThreatToPlayer(pa)
                                  select ((Thing)pa).Faction into fa
                                  where fa != null && !fa.HostileTo(faction)
                                  // Faction doesn't have own GetHashCode or Equals, so Distinct below works on reference equality.
                                  // ALthough this is iffy, this is what vanilla RW does, so leave it be.
                                  select fa).Distinct();
                    var key = "MilitaryAidConfirmMutualEnemy";
                    var diaNode = new DiaNode(key.Translate(faction.Name,
                        GenText.ToCommaList(from fa in source select fa.Name, true)));
                    var diaOption3 = new DiaOption("CallConfirm".Translate())
                    {
                        action = () => CallForAid(map),
                        link = FightersSent(),
                    };
                    var diaOption4 = new DiaOption("CallCancel".Translate())
                    {
                        linkLateBind = ResetToRoot(),
                    };
                    diaNode.options.Add(diaOption3);
                    diaNode.options.Add(diaOption4);
                    diaOption2.link = diaNode;
                    return diaOption2;
                }
            }
            diaOption2.action = () => CallForAid(map);
            diaOption2.link = FightersSent();
            return diaOption2;
        }

        private static DiaNode FightersSent()
        {
            return new DiaNode("MilitaryAidSent".Translate(faction.leader.LabelIndefinite()).CapitalizeFirst())
            {
                options =
                {
                    OKToRoot(),
                },
            };
        }

        private static void CallForAid(Map map)
        {
            PlaySoundFor(faction);
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -25);
            var incidentParms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = Rand.Range(150, 400),
            };
            IncidentDefOf.RaidFriendly.Worker.TryExecute(incidentParms);
            HarmonyPatches.lastPhoneAideFaction = faction;
            HarmonyPatches.lastPhoneAideTick = Find.TickManager.TicksGame;
        }

        public static void PlaySoundFor(Faction faction)
        {
            if (faction.def.GetFactionSettings() is FactionSettings fs)
            {
                fs.entrySoundDef?.PlayOneShotOnCamera();
            }
        }

        private static DiaOption OKToRoot()
        {
            return new DiaOption("OK".Translate())
            {
                linkLateBind = ResetToRoot(),
            };
        }

        private static Func<DiaNode> ResetToRoot()
        {
            return () => FactionDialogFor(negotiator, faction);
        }

        private static DiaNode root;

        private static Pawn negotiator;

        private static Faction faction;

        private const float MinRelationsToCommunicate = -70f;

        private const float MinRelationsFriendly = 40f;

        private const int GiftSilverAmount = 300;

        private const float GiftSilverGoodwillChange = 12f;

        private const float MilitaryAidRelsChange = -25f;

        private const int TradeRequestCost_Wary = 1100;

        private const int TradeRequestCost_Warm = 700;

        private static bool TryStartPeaceTalks(Faction faction)
        {
            if (!TryFindTile(out var tile))
            {
                return false;
            }
            var peaceTalks = (PeaceTalks)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PeaceTalks);
            peaceTalks.Tile = tile;
            peaceTalks.SetFaction(faction);
            peaceTalks.GetComponent<TimeoutComp>().StartTimeout(900000);
            Find.WorldObjects.Add(peaceTalks);
            var def = IncidentDef.Named("QuestPeaceTalks"); // XXX: this doesn't exist anymore - see comment on class
            var text = string.Format(def.letterText.AdjustedFor(faction.leader), faction.def.leaderTitle, faction.Name, 15).CapitalizeFirst();
            Find.LetterStack.ReceiveLetter(def.letterLabel, text, def.letterDef, peaceTalks, null);
            return true;
        }

        private static bool TryFindTile(out int tile) => TileFinder.TryFindNewSiteTile(out tile, 5, 15, false, false, -1);

        private static bool PeaceTalksExist(Faction faction)
        {
            var peaceTalks = Find.WorldObjects.PeaceTalks;
            for (var i = 0; i < peaceTalks.Count; i++)
                if (peaceTalks[i].Faction == faction)
                    return true;
            return false;
        }
    }
}
