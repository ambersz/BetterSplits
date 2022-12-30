using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Kitchen;
using KitchenData;
using KitchenMods;
using Unity.Entities;
using UnityEngine.Android;
using static BetterSplits.LiveSplitUtils;

namespace BetterSplits
{
    public struct SBetterSplitState
    {
        public string currentSplitName;
        public Dictionary<int, List<string>> NotableAppliancesByDay;
    }
    static public class LiveSplitUtils
    {
        private static bool debug = false;
        public static SBetterSplitState state = new SBetterSplitState
        {
            currentSplitName="",
            NotableAppliancesByDay= new Dictionary<int, List<string>>()
        };
        public static void Send(string message)
        {
            bool flag = !LiveSplit.LiveSplit.IsConnected;
            if (!flag)
            {
                try
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(message + "\r\n");
                    LiveSplit.LiveSplit.Socket.Send(bytes);
                }
                catch (SocketException ex)
                {
                    bool flag2 = LiveSplit.LiveSplit.Socket != null;
                    if (flag2)
                    {
                        LiveSplit.LiveSplit.Disconnect(true);
                    }
                    UnityEngine.Debug.LogWarning(ex.ErrorCode);
                }
            }
        }
        public static void Log(string l)
        {
            if (debug)
                UnityEngine.Debug.Log(l);
        }

        public static string StartOfDayMessage(int day)
        {
            return $"Day {day} Prep";
        }
        public static string EndOfDayMessage(int day)
        {

            return $"Day {day}";
        }
        static public string GetCurrentSplitName()
        {
            Send("getcurrentsplitname");
            var s = LiveSplit.LiveSplit.Socket;
            byte[] RecvBytes = new byte[256];
            int bytes = s.Receive(RecvBytes, RecvBytes.Length, 0);
            string name = ""+ Encoding.ASCII.GetString(RecvBytes, 0, bytes);

            while (s.Available>0) // receive more data only when there's actually more data to read
            {
                bytes = s.Receive(RecvBytes, RecvBytes.Length, 0);
                name = name + Encoding.ASCII.GetString(RecvBytes, 0, bytes);
            }
            return name.Substring(0, name.Length - 2); // ends with \r\n
        }
        public static string GetCurrentSplitIndex()
        {
            Send("getsplitindex");
            var s = LiveSplit.LiveSplit.Socket;
            byte[] RecvBytes = new byte[256];
            int bytes = s.Receive(RecvBytes, RecvBytes.Length, 0);
            string index = ""+ Encoding.ASCII.GetString(RecvBytes, 0, bytes);

            while (s.Available>0) // receive more data only when there's actually more data to read
            {
                bytes = s.Receive(RecvBytes, RecvBytes.Length, 0);
                index = index + Encoding.ASCII.GetString(RecvBytes, 0, bytes);
                Log($"current split index getting data of size: {bytes}, progress: {index}");
            }
            Log($"current split index: {index}");
            return index;

        }
        public static SBetterSplitState GetState(this GameSystemBase rs)
        {
            return state;
        }
        public static void SetState(this GameSystemBase rs, SBetterSplitState s)
        {
            state = s;
        }
        public static void SkipUntil(this GameSystemBase rs, string expectedSplitName, bool requireBuy = false)
        {
            Log($"SkipUntil {expectedSplitName} {requireBuy}");
            var state = rs.GetState();
            while (expectedSplitName!= state.currentSplitName)
            {
                // LogOnce($"Skipping because expected {expectedSplitName} but the current split name is {currentSplitName}");
                if (requireBuy)
                {
                    if (!state.currentSplitName.StartsWith("Buy "))
                    {
                        // Nothing worth skipping over any more, give up!
                        break;
                    }
                }
                Send("skipsplit");
                state.currentSplitName = GetCurrentSplitName();
            }
            rs.SetState(state);

        }
        public static void SkipUntilThenSplit(this GameSystemBase rs, string expectedSplitName, bool requireBuy = false)
        {
            Log($"SkipUntilThenSplit {expectedSplitName} {requireBuy}");
            rs.SkipUntil(expectedSplitName, requireBuy);
            var state = rs.GetState();
            LiveSplit.LiveSplit.SendSplit();
            state.currentSplitName = GetCurrentSplitName();
            rs.SetState(state);
            return;

        }

        public static void UpdateSplitName(this GameSystemBase rs)
        {
            var state = rs.GetState();
            state.currentSplitName = GetCurrentSplitName();
            rs.SetState(state);
        }


        public static bool Enabled()
        {
            return Preferences.Get<bool>(Pref.LiveSplitEnabled);
        }
    }
    /*
    public struct STestSingleton : IModComponent
    {
        //public string name;
    }
    public class TestSystem : RestaurantInitialisationSystem, IModSystem
    {
        protected override void Initialise()
        {
            UnityEngine.Debug.Log("test initialized");
            base.Initialise();
            STestSingleton nothing; // should be empty
            bool got = TryGetSingleton(out nothing);

            UnityEngine.Debug.Log($"got: {got}");
            UnityEngine.Debug.Log("test initialized 2");
        }
        protected override void OnUpdate()
        {

        }
    }
    //*/
    
    [UpdateBefore(typeof(LiveSplitIntegration))]
    public class BetterSplitInit : StartOfDaySystem, IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
            UnityEngine.Debug.Log("BetterSplits Mod Initialized");
            // this.UpdateSplitName();
        }
        protected override void OnUpdate()
        {
            Log("BetterSplitInit OnUpdate");
            if (!Enabled())
            {
                return;
            }
            if (GetSingleton<SDay>().Day!=1)
                return;
            //*
            RecordItems();
            //*/
        }

        public void ResetConfig()
        {
            Clear<SSplitOnStart>();
            Clear<SSplitOnPurchase>();
            Clear<SSplitOnGroups>();
            Set<SUpdateSegmentName>();
            state.currentSplitName = "";
            state.NotableAppliancesByDay.Clear();
        }
        public void RecordItems()
        {
            Send("reset"); // reset the timer in case it was left running for something else
            Send("starttimer"); // can't skip splits unless the timer is actually running
            this.ResetConfig(); // clear state from previous run
            var state = this.GetState();

            string previousSplitIndex = "";
            string currentSplitIndex = GetCurrentSplitIndex();
            int day = 0;
            List<string> notableItems = new List<string>();
            while (previousSplitIndex != currentSplitIndex)
            {
                string readSplitName = GetCurrentSplitName();
                Log($"Segment: `{readSplitName}`");
                Log($"StartOfDayMessage: {EndOfDayMessage(day+1)}");
                if (readSplitName == EndOfDayMessage(day+1))
                {
                    Log($"finish day {day}");
                    // finish day
                    if (notableItems.Count>0)
                    {
                        state.NotableAppliancesByDay.Add(day, notableItems);
                        notableItems = new List<string>();
                    }
                    day++;
                }
                else if (readSplitName.StartsWith("Buy "))
                {
                    Log($"Buy segment name read");
                    notableItems.Add(readSplitName.Substring(4));
                }
                else if (readSplitName.EndsWith("Prep"))
                {
                    Log($"Prep segment name read");
                    Set<SSplitOnStart>();
                    Log("set ssplitonstart");
                }
                else if (readSplitName == "Group Leaves")
                {
                    Log($"Leaving segment name read");
                    Set<SSplitOnGroups>();
                    Log("set ssplitongroups");
                }

                previousSplitIndex = currentSplitIndex;
                Send("skipsplit");
                currentSplitIndex = GetCurrentSplitIndex();
            }
            Log($"{state.NotableAppliancesByDay.Count} days I buy appliances on");
            if (state.NotableAppliancesByDay.Count >0)
            {
                Set<SSplitOnPurchase>();
                Log("set ssplitonpurschase");
            }
            // done gathering data, reset the timer.
            this.SetState(state);
            Send("reset");
        }

    }
    public struct SUpdateSegmentName : IModComponent
    {
    }
    public struct SSplitOnStart : IModComponent
    {
    }
    public struct SSplitOnGroups : IModComponent
    {
    }
    public struct SSplitOnPurchase : IModComponent
    {
    }

    [UpdateAfter(typeof(LiveSplitIntegration))]
    public class SplitStartOfDaySystem : StartOfDaySystem, IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
            RequireSingletonForUpdate<SSplitOnStart>();
        }
        protected override void OnUpdate()
        {
            if (!Enabled())
                return;
            Log("Updating for start of day");
            if (Has<SPracticeMode>())
            {
                Log("Don't Split for practice mode");
                return;
            }
            var day = GetSingleton<SDay>();
            this.UpdateSplitName();
            if (day.Day==1)
            {
                return; // start of first day, built-in integration started the run
            }
            var expectedSplitName = StartOfDayMessage(day.Day);
            this.SkipUntilThenSplit(expectedSplitName);
        }
    }

    [UpdateBefore(typeof(GroupHandleStartLeaving))]
    [UpdateAfter(typeof(GroupHandleChoosingOrder))]
    [UpdateInGroup(typeof(UpdateCustomerStatesGroup))]
    public class SplitOnLeaveSystem : DaySystem, KitchenMods.IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
            GroupLeaves = GetEntityQuery(GroupLeavesDesc);
            RequireForUpdate(GroupLeaves);
            RequireSingletonForUpdate<SSplitOnGroups>();
        }
        private EntityQuery GroupLeaves;
        private EntityQueryDesc GroupLeavesDesc = new EntityQueryDesc
        {
            All =
                new ComponentType[]
                {
                    ComponentType.ReadOnly<CGroupStartLeaving>(),
                    // ComponentType.ReadOnly<CGroupStateChanged>(),
                }
        };
        protected override void OnUpdate()
        {
            if (!Enabled())
                return;
            //  Log("SplitOnLeave OnUpdate");
            if (!GroupLeaves.IsEmpty)
            {
                Log("SplitOnLeave Action Time!");
                var state = this.GetState();
                // What could be here....? If you have more groups than expected it may be the end of day split... just skip splitting if it's not what you expected to see
                int times = GroupLeaves.CalculateEntityCount();
                for (int i = 0; i < times; i++)
                {
                    if (state.currentSplitName != "Group Leaves")
                        return;
                    this.SkipUntilThenSplit("Group Leaves");
                }
            }
        }
    }
    [UpdateBefore(typeof(LiveSplitIntegration))]
    public class EndOfDaySyncSystem : StartOfNightSystem, IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
            // What is uncertain/ could cause out of sync issues?
            // start of day is guaranteed if it exists.
            // So:
            // 1. if StartOfDay, need to SplitOnGroups
            // 1. if NO StartOfDay, either SplitOnGroups or SplitOnPurchase could cause out of sync
            RequireForUpdate(GetEntityQuery(new EntityQueryDesc[]
            {
                new EntityQueryDesc
                {
                    All = new ComponentType[]
                    {
                        typeof(SSplitOnStart),
                        typeof(SSplitOnGroups),
                    }
                },
                new EntityQueryDesc
                {
                    Any= new ComponentType[]
                    {
                        typeof(SSplitOnGroups),
                        typeof(SSplitOnPurchase)
                    },
                    None = new ComponentType[]
                    {
                        typeof(SSplitOnStart),
                    }
                },
            }));
        }
        protected override void OnUpdate()
        {
            if (!Enabled())
                return;
            var day = GetSingleton<SDay>().Day;
            this.SkipUntil(EndOfDayMessage(day));
        }
    }
    [UpdateAfter(typeof(LiveSplitIntegration))]
    public class SyncSegmentNameSystem : StartOfNightSystem, IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
        }
        protected override void OnUpdate()
        {
            if (!Enabled())
                return;
            this.UpdateSplitName();
        }
    }

    // [UpdateAfter(typeof(PurchaseAfterDuration))] // maybe I don't need to specify this?
    [UpdateInGroup(typeof(CreationGroup))]
    [UpdateBefore(typeof(CreateNewAppliances))]
    public class SplitOnPurchaseSystem : NightSystem, IModSystem
    {
        protected override void Initialise()
        {
            base.Initialise();
            Purchase = GetEntityQuery(PurchaseDesc);
            RequireSingletonForUpdate<SSplitOnPurchase>();
            RequireForUpdate(Purchase);
        }
        private EntityQuery Purchase;
        private EntityQueryDesc PurchaseDesc = new EntityQueryDesc
        {
            All =  new ComponentType[]
                {
                    ComponentType.ReadOnly<CCreateAppliance>(),
                    ComponentType.ReadOnly<CHeldBy>(),
                }
        };
        protected override void OnUpdate()
        {
            if (!Enabled())
                return;
            var day = GetSingleton<SDay>().Day;
            var state = this.GetState();
            if (!state.NotableAppliancesByDay.ContainsKey(day))
                return; // we're not checking for any purchases today.
            var notableAppliances = state.NotableAppliancesByDay[day];
            var purchases = Purchase.ToComponentDataArray<CCreateAppliance>(Unity.Collections.Allocator.Temp);
            foreach (var p in purchases)
            {
                var name = GameData.Main.Get<Appliance>(p.ID).Name;
                if (notableAppliances.Contains(name))
                {
                    while (state.currentSplitName != $"Buy {name}")
                    {
                        notableAppliances.Remove(state.currentSplitName.Substring(4));
                        Send("skipsplit");
                        state.currentSplitName = GetCurrentSplitName();
                    }
                    Send("split");
                    state.currentSplitName = GetCurrentSplitName();
                    notableAppliances.Remove(name);
                }
            }
        }
    }
}
