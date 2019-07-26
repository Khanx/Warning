using Pipliz;

using Chatting;
using NetworkUI;
using NetworkUI.Items;

using System.Collections.Generic;
using System.Linq;
using Pipliz.JSON;
using System.IO;

using Newtonsoft.Json.Linq;

namespace RunOutWarning
{
    //Add a command (/warning) which will launch the UI to set the amount for a warning
    [ChatCommandAutoLoader]
    public class WarningCommand : IChatCommand
    {
        public bool TryDoCommand(Players.Player player, string chat, List<string> splits)
        {
            if(player == null)
                return false;

            if(!"/warning".Equals(chat.Trim().ToLower()))
                return false;

            //Sends the UI to the player
            Warning.SendSetWarningsUI(player);

            return true;
        }
    }

    //Add a command (/warning) which will launch the UI that show items running out
    [ChatCommandAutoLoader]
    public class SeeWarningsCommand : IChatCommand
    {
        public bool TryDoCommand(Players.Player player, string chat, List<string> splits)
        {
            if(player == null)
                return false;

            if(!"/seewarnings".Equals(chat.Trim().ToLower()))
                return false;

            //Sends the UI to the player
            Warning.SendSeeWarningsUI(player);

            return true;
        }
    }

    [ModLoader.ModManager]
   public class Warning
    {
        //Warning "Manager", it saves all the warning clases (Important: must be STATIC
        public static Dictionary<NetworkID, Dictionary<ushort, int>> warnings = new Dictionary<NetworkID, Dictionary<ushort, int>>();

        //Time to check types in stockpile
        private static long nextUpdate = 0;
        private static long increment = 240000; //240000 = 4 min check & send message

        //Check every <increment> (miliseconds) the amount of the types observed
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnUpdate, "Khanx.Warning.OnUpdate")]
        public static void OnUpdate()
        {
            if(Time.MillisecondsSinceStart < nextUpdate)
                return;

            nextUpdate = Time.MillisecondsSinceStart + increment;

            foreach(NetworkID pl in warnings.Keys)
            {
                if(!Players.TryGetPlayer(pl, out Players.Player player))
                    continue;

                if(!player.ConnectionState.Equals(Players.EConnectionState.Connected))
                    continue;

                Dictionary<ushort, int> warning = warnings[pl];

                if(player.ActiveColony == null)
                    continue;

                Stockpile warehouse = player.ActiveColony.Stockpile;
                foreach(ushort itemIndex in warning.Keys)
                {
                    int amount = warning[itemIndex];

                    //Chat.SendToConnected("Cheking:" + ItemTypes.IndexLookup.GetName(itemIndex));
                    //Chat.SendToConnected("Required:" + amount);
                    //Chat.SendToConnected("You have:" + warehouse.Items.GetValueOrDefault(itemIndex, 0));

                    if(warehouse.Items.GetValueOrDefault(itemIndex, 0) < amount)
                        Chat.Send(player, "<color=red>Warning: You need more: " + ItemTypes.IndexLookup.GetName(itemIndex) + ", current amount: " + warehouse.Items.GetValueOrDefault(itemIndex, 0) + "</color>");
                }
            }
        }

        public static List<string> PreferredCategories = new List<string> { "essential", "sciencebag", "food", "job", "ingredient", "decorative" };

        //Sends the interface of the category PreferredCategories[<category>]
        public static void SendSetWarningsUI(Players.Player player, int category = 0)
        {
            NetworkMenu menu = new NetworkMenu();
            menu.Identifier = "WarningUI";
            menu.Width = 400;
            menu.Height = 600;

            Label label = new Label("Category: ");
            DropDownNoLabel dropDown = new DropDownNoLabel("Khanx.Warning.categoryDropDown", PreferredCategories);
            //Default dropdown (ALWAYS INCLUDE OR GIVES ERROR)
            menu.LocalStorage.SetAs("Khanx.Warning.categoryDropDown", category);

            //Kenovis: NEEDS JTOKEN
            //JToken jToken = JToken.Parse("Khanx.Warning.ChangeCategory");

            ButtonCallback buttonCallback = new ButtonCallback("Khanx.Warning.ChangeCategory", new LabelData("Change category", UnityEngine.Color.black));

            List<(IItem, int)> horizontalRowItems = new List<(IItem, int)>();

            horizontalRowItems.Add((label, 75));
            horizontalRowItems.Add((dropDown, 125));
            horizontalRowItems.Add((buttonCallback, 125));

            HorizontalRow horizontalRow = new HorizontalRow(horizontalRowItems);

            menu.Items.Add(horizontalRow);
            menu.Items.Add(new EmptySpace(25));

            //Show icon & amount per category
            IEnumerable<ItemTypes.ItemType> itemsInCategory = ItemTypes.NotableTypes.Where(type => type.HasCategory(PreferredCategories[category]));

            Dictionary<ushort, int> warning = warnings.GetValueOrDefault(player.ID, new Dictionary<ushort, int>());

            foreach(ItemTypes.ItemType item in itemsInCategory)
            {
                ItemIcon icon = new ItemIcon(item.Name);
                InputField inputField = new InputField("Khanx.Warning." + item.Name + "Field", 50);

                //default value
                menu.LocalStorage.SetAs("Khanx.Warning." + item.Name + "Field", warning.GetValueOrDefault(item.ItemIndex, 0));

                menu.Items.Add(new HorizontalSplit(icon, inputField));
            }

            NetworkMenuManager.SendServerPopup(player, menu);
        }

        public static void SendSeeWarningsUI(Players.Player player)
        {
            if(player == null)
                return;

            if(player.ActiveColony == null)
            {
                Chat.Send(player, "You are not in a colony.");
                return;
            }

            if(!warnings.ContainsKey(player.ID))
            {
                Chat.Send(player, "You have not set any warning.");
                return;
            }

            if(!warnings.TryGetValue(player.ID, out Dictionary<ushort, int> warning))
            {
                Chat.Send(player, "You have not set any warning.");
                return;
            }

            NetworkMenu menu = new NetworkMenu();
            menu.Identifier = "WarningUI";
            menu.Width = 400;
            menu.Height = 600;

            Stockpile warehouse = player.ActiveColony.Stockpile;
            foreach(ushort itemIndex in warning.Keys)
            {
                int amount = warning[itemIndex];

                if(warehouse.Items.GetValueOrDefault(itemIndex, 0) < amount)
                    menu.Items.Add(new ItemIcon(itemIndex));
            }

            NetworkMenuManager.SendServerPopup(player, menu);
        }

        //Load Warnings on start server
        [ModLoader.ModCallback(ModLoader.EModCallbackType.AfterWorldLoad, "Khanx.Warning.LoadWarnings")]
        public static void LoadWarnings()
        {
            Log.Write("<color=lime>Loading warnings</color>");
            string jsonFilePath = "./gamedata/savegames/" + ServerManager.WorldName + "/warning.json";

            if(!File.Exists(jsonFilePath))
                return;

            JSONNode warninJSON = JSON.Deserialize(jsonFilePath);

            foreach(JSONNode warn in warninJSON.LoopArray())
            {
                NetworkID player = NetworkID.Parse(warn.GetAs<string>("player"));

                Dictionary<ushort, int> warning = new Dictionary<ushort, int>();

                //Load checkItems
                foreach(var checkItem in warn.GetAs<JSONNode>("checkItems").LoopArray())
                {
                    ushort type = checkItem.GetAs<ushort>("type");
                    int amount = checkItem.GetAs<int>("amount");

                    warning.Add(type, amount);
                }

                warnings.Add(player, warning);
            }
        }

        //Save Warnings
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnAutoSaveWorld, "Khanx.Warning.SaveOnAutoSave")]
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnQuit, "Khanx.Warning.SaveOnQuit")]
        public static void SaveWarnings()
        {
            Log.Write("<color=lime>Saving warnings</color>");
            string jsonFilePath = "./gamedata/savegames/" + ServerManager.WorldName + "/warning.json";

            if(File.Exists(jsonFilePath))
                File.Delete(jsonFilePath);

            if(warnings.Count == 0)
                return;

            JSONNode warningsJSON = new JSONNode(NodeType.Array);

            foreach(var key in warnings.Keys)
            {
                JSONNode warningJSON = new JSONNode();

                warningJSON.SetAs<string>("player", key.ToString());

                JSONNode checkItems = new JSONNode(NodeType.Array);

                foreach(var type in warnings[key].Keys)
                {
                    JSONNode checkItem = new JSONNode();
                    checkItem.SetAs<ushort>("type", type);
                    checkItem.SetAs<int>("amount", warnings[key][type]);

                    checkItems.AddToArray(checkItem);
                }

                warningJSON.SetAs("checkItems", checkItems);
                warningsJSON.AddToArray(warningJSON);
            }

            JSON.Serialize(jsonFilePath, warningsJSON);
        }

    }

    [ModLoader.ModManager]
    public static class WarningUIInteraction
    {
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerPushedNetworkUIButton, "Khanx.Warning.OnPlayerPushedNetworkUIButton")]
        public static void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
        {
            if(data.ButtonIdentifier.StartsWith("Khanx.Warning."))
            {
                int category = data.Storage.GetAs<int>("Khanx.Warning.categoryDropDown");

                Warning.SendSetWarningsUI(data.Player, category);
            }
        }

        /*TODO
         *
         *  Manage the input of inputfields to only catch numbers
         */
        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerEditedNetworkInputfield, "Khanx.Warning.OnPlayerEditedNetworkInputfield")]
        public static void OnPlayerEditedNetworkInputfield(InputfieldEditCallbackData data)
        {
            Dictionary<ushort, int> warning = Warning.warnings.GetValueOrDefault(data.Player.ID, new Dictionary<ushort, int>());

            if(data.InputfieldIdentifier.StartsWith("Khanx.Warning."))
            {
                IEnumerable<ItemTypes.ItemType> itemsInCategory = ItemTypes.NotableTypes.Where(type => type.HasCategory(Warning.PreferredCategories[data.Storage.GetAs<int>("Khanx.Warning.categoryDropDown")]));

                foreach(ItemTypes.ItemType item in itemsInCategory)
                {
                    data.Storage.TryGetAsOrDefault<int>("Khanx.Warning." + item.Name + "Field", out int amount, 0);
                    int warningAmount = warning.GetValueOrDefault(item.ItemIndex, 0);

                    if(amount != warningAmount)
                    {
                        if (warning.ContainsKey(item.ItemIndex))
                            warning.Remove(item.ItemIndex);

                        warning.Add(item.ItemIndex, amount);
                    }
                }

                if(Warning.warnings.ContainsKey(data.Player.ID))
                    Warning.warnings.Remove(data.Player.ID);

                Warning.warnings.Add(data.Player.ID, warning);
            }
        }
    }
}
