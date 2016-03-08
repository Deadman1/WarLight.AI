﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarLight.AI.Prod
{
    public class BotMain : IWarLightAI
    {
        public GameStanding DistributionStandingOpt;
        public GameStanding Standing;
        public PlayerIDType PlayerID;
        public Dictionary<PlayerIDType, GamePlayer> Players;
        public MapDetails Map;
        public GameSettings Settings;
        public Dictionary<PlayerIDType, TeammateOrders> TeammatesOrders;
        public List<CardInstance> Cards;
        public int CardsMustPlay;
        public Dictionary<PlayerIDType, PlayerIncome> Incomes;
        public PlayerIncome BaseIncome;
        public PlayerIncome EffectiveIncome;

        public List<GamePlayer> Opponents;
        public bool IsFFA; //if false, we're in a 1v1, 2v2, 3v3, etc.  If false, there are more than two entities still alive in the game.  A game can change from FFA to non-FFA as players are eliminated.
        public Dictionary<PlayerIDType, Neighbor> Neighbors;
        public bool NoRandomness = false;


        //not available during picking:
        public MakeOrders.MakeOrdersMain MakeOrders; 
        public MakeOrders.OrdersManager Orders { get { return MakeOrders.Orders; } }

        public void Init(PlayerIDType myPlayerID, Dictionary<PlayerIDType, GamePlayer> players, MapDetails map, GameStanding distributionStanding, GameSettings gameSettings, int numberOfTurns, Dictionary<PlayerIDType, PlayerIncome> incomes, GameOrder[] prevTurn, GameStanding latestTurnStanding, GameStanding previousTurnStanding, Dictionary<PlayerIDType, TeammateOrders> teammatesOrders, List<CardInstance> cards, int cardsMustPlay)
        {
            this.DistributionStandingOpt = distributionStanding;
            this.Standing = latestTurnStanding;
            this.PlayerID = myPlayerID;
            this.Players = players;
            this.Map = map;
            this.Settings = gameSettings;
            this.TeammatesOrders = teammatesOrders;
            this.Cards = cards;
            this.CardsMustPlay = cardsMustPlay;
            this.Incomes = incomes;
            this.BaseIncome = Incomes[PlayerID];
            this.EffectiveIncome = BaseIncome.Clone();
            this.Neighbors = players.Keys.ExceptOne(PlayerID).ConcatOne(TerritoryStanding.NeutralPlayerID).ToDictionary(o => o, o => new Neighbor(this, o));
            this.Opponents = players.Values.Where(o => o.State == GamePlayerState.Playing && !IsTeammateOrUs(o.ID)).ToList();
            this.IsFFA = Opponents.Count > 1 && (Opponents.Any(o => o.Team == PlayerInvite.NoTeam) || Opponents.GroupBy(o => o.Team).Count() > 1);
        }

        public int ArmiesToTake(Armies defenseArmies)
        {
            var ret = SharedUtility.Round((defenseArmies.DefensePower / Settings.OffensiveKillRate) - 0.5);

            if (ret == SharedUtility.Round(defenseArmies.DefensePower * Settings.DefensiveKillRate))
                ret++;

            if (Settings.RoundingMode == RoundingModeEnum.WeightedRandom)
                ret++;

            if (Settings.LuckModifier < 1)
                ret += SharedUtility.Round((1.0 - Settings.LuckModifier) / 10.0 * ret); //Add up to 10% more armies to account for luck

            return ret;
        }

        public List<GameOrder> GetOrders()
        {
            MakeOrders = new MakeOrders.MakeOrdersMain(this);
            return MakeOrders.Go();
        }

        public List<TerritoryIDType> GetPicks()
        {
            return MakePicks.PickTerritories.MakePicks(this);
        }


        public string TerrString(TerritoryIDType terrID)
        {
            return Map.Territories[terrID].Name + " (" + terrID + ")";
        }
        public string BonusString(BonusIDType bonusID)
        {
            return BonusString(Map.Bonuses[bonusID]);
        }
        public string BonusString(BonusDetails bonus)
        {
            return bonus.Name + " (id=" + bonus.ID + " val=" + BonusValue(bonus.ID) + ")";
        }
        public GamePlayer GamePlayerReference
        {
            get { return Players[PlayerID]; }
        }
        public bool IsTeammate(PlayerIDType playerID)
        {
            return Players[PlayerID].Team != PlayerInvite.NoTeam && Players.ContainsKey(playerID) && Players[playerID].Team == Players[PlayerID].Team;
        }
        public bool IsTeammateOrUs(PlayerIDType playerID)
        {
            return PlayerID == playerID || IsTeammate(playerID);
        }
        public int BonusValue(BonusIDType bonusID)
        {
            if (Settings.OverriddenBonuses.ContainsKey(bonusID))
                return Settings.OverriddenBonuses[bonusID];
            else
                return Map.Bonuses[bonusID].Amount;
        }

        public IEnumerable<GameOrder> TeammatesSubmittedOrders
        {
            get
            {
                if (TeammatesOrders == null)
                    return new GameOrder[0];
                else
                    return TeammatesOrders.Values.Where(o => o.Orders != null).SelectMany(o => o.Orders);
            }
        }

        public IEnumerable<TerritoryStanding> Territories
        {
            get { return Standing.Territories.Values; }
        }

        public IEnumerable<TerritoryStanding> AttackableTerritories
        {
            get { return Territories.Where(o => Map.Territories[o.ID].ConnectedTo.Any(c => Standing.Territories[c].OwnerPlayerID == PlayerID)); }
        }

        /// <summary>
        /// Territories of ours that aren't entirely enclosed by our own
        /// </summary>
        public IEnumerable<TerritoryStanding> BorderTerritories
        {
            get
            {
                return Territories
                    .Where(o => Standing.Territories[o.ID].OwnerPlayerID == PlayerID)
                    .Where(o => this.Map.Territories[o.ID].ConnectedTo
                        .Any(c => this.Standing.Territories[c].OwnerPlayerID != this.PlayerID));
            }
        }


        /// <summary>
        /// Returns 0 if it is an ememy, and a positive number otherwise signaling how many turns away from an enemy it is
        /// </summary>
        /// <param name="terrID"></param>
        /// <returns></returns>
        public int DistanceFromEnemy(TerritoryIDType terrID)
        {
            if (IsTeammateOrUs(Standing.Territories[terrID].OwnerPlayerID) == false)
                return 0;

            var terrIDs = new HashSet<TerritoryIDType>();
            terrIDs.Add(terrID);

            var distance = 1;

            while (true)
            {
                var toAdd = terrIDs.SelectMany(o => Map.Territories[o].ConnectedTo).Except(terrIDs).ToList();

                if (toAdd.Count == 0)
                    return int.MaxValue; //no enemies found on the entire map

                if (toAdd.Any(o => Standing.Territories[o].IsNeutral == false && IsTeammateOrUs(Standing.Territories[o].OwnerPlayerID) == false))
                    break; //found an enemy

                terrIDs.AddRange(toAdd);
                distance++;
            }

            return distance;
        }


        public TerritoryIDType OurNearestSpotTo(TerritoryIDType terr)
        {
            if (Standing.Territories[terr].OwnerPlayerID == PlayerID)
                return terr;

            var visited = new HashSet<TerritoryIDType>();
            visited.Add(terr);

            bool addedOne;

            do
            {
                addedOne = false;

                foreach (var front in visited.ToList())
                {
                    var connections = Map.Territories[front].ConnectedTo.Where(o => !visited.Contains(o)).ToList();
                    connections.RandomizeOrder();

                    foreach (var conn in connections)
                    {
                        if (Standing.Territories[conn].OwnerPlayerID == PlayerID)
                            return conn;

                        visited.Add(conn);
                        addedOne = true;
                    }
                }
            }
            while (addedOne);

            throw new Exception("Could not find any territories of ours");
        }


        public bool PlayerControlsBonus(BonusDetails b)
        {
            var c = b.ControlsBonus(Standing);
            return c.HasValue && c.Value == PlayerID;
        }


        public TerritoryIDType? MoveTowardsNearestBorder(TerritoryIDType id)
        {
            var neighborDistances = new KeyValueList<TerritoryIDType, int>();

            foreach (var immediateNeighbor in Map.Territories[id].ConnectedTo)
            {
                var nearestBorder = FindNearestBorder(immediateNeighbor, new Nullable<TerritoryIDType>(id));
                if (nearestBorder != null)
                    neighborDistances.Add(immediateNeighbor, nearestBorder.Depth);
            }

            if (neighborDistances.Count == 0)
                return null;

            var ret = neighborDistances.GetKey(0);
            int minValue = neighborDistances.GetValue(0);

            for (int i = 1; i < neighborDistances.Count; i++)
            {
                if (neighborDistances.GetValue(i) < minValue)
                {
                    ret = neighborDistances.GetKey(i);
                    minValue = neighborDistances.GetValue(i);
                }
            }

            return ret;
        }

        public class FindNearestBorderResult
        {
            public TerritoryIDType NearestBorder;
            public int Depth;
        }

        public FindNearestBorderResult FindNearestBorder(TerritoryIDType id, TerritoryIDType? exclude)
        {
            var queue = new Queue<TerritoryIDType>();
            queue.Enqueue(id);
            var visited = new HashSet<TerritoryIDType>();
            if (exclude.HasValue)
                visited.Add(exclude.Value);

            int depth = 0;

            while (true)
            {
                TerritoryIDType? r = FindNearestBorderRecurse(queue, visited);
                if (r.HasValue)
                {
                    FindNearestBorderResult ret = new FindNearestBorderResult();
                    ret.NearestBorder = r.Value;
                    ret.Depth = depth;
                    return ret;
                }

                depth++;

                if (queue.Count == 0)
                    return null; //No border

            }

#if CS2HX || CSSCALA
            throw new Exception("Never");
#endif
        }

        private TerritoryIDType? FindNearestBorderRecurse(Queue<TerritoryIDType> queue, HashSet<TerritoryIDType> visited)
        {

            var id = queue.Dequeue();

            if (Map.Territories[id].ConnectedTo.Any(o => !this.IsTeammateOrUs(this.Standing.Territories[o].OwnerPlayerID)))
                return id; //We're a border

            foreach (var notVisited in Map.Territories[id].ConnectedTo.Where(o => !visited.Contains(o)))
            {
                queue.Enqueue(notVisited);
                visited.Add(notVisited);
            }

            return null;
        }



        public bool OpponentMightControlBonus(BonusDetails b)
        {
            PlayerIDType? oppID = null;
            foreach (var territoryID in b.Territories)
            {
                var ts = Standing.Territories[territoryID];
                if (ts.OwnerPlayerID == TerritoryStanding.FogPlayerID)
                    continue;

                if (ts.OwnerPlayerID == TerritoryStanding.AvailableForDistribution || ts.OwnerPlayerID == TerritoryStanding.NeutralPlayerID || IsTeammateOrUs(ts.OwnerPlayerID))
                    return false;
                if (!oppID.HasValue)
                    oppID = ts.OwnerPlayerID;
                else if (oppID.Value != ts.OwnerPlayerID)
                    return false; //nobody has it
            }

            return true;
        }
    }
}
