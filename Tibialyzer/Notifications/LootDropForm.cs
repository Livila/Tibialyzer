
// Copyright 2016 Mark Raasveldt
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace Tibialyzer {
    public partial class LootDropForm : NotificationForm {
        public List<Tuple<Item, int>> items;
        public Dictionary<Creature, int> creatures;

        public Dictionary<Item, List<PictureBox>> itemControls = new Dictionary<Item, List<PictureBox>>();
        public Dictionary<Creature, Tuple<PictureBox, Label>> creatureControls = new Dictionary<Creature, Tuple<PictureBox, Label>>();
        public Creature lootCreature;
        public Hunt hunt;
        public int initialPage = 0;
        public int page = 0;
        public const int pageHeight = 400;
        public const int maxCreatureHeight = 700;
        public const int minLootWidth = 203;
        private string huntName = "";
        private string creatureName = "";
        public string rawName = "";
        private long averageGold = 0;
        private object updateLock = new object();
        private static System.Timers.Timer updateTimer;

        ToolTip value_tooltip = new ToolTip();
        public static Font loot_font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold);
        public LootDropForm(string command) {
            string[] split = command.Split('@');
            if (split.Length >= 2) {
                huntName = split[1];
            }
            if (split.Length >= 3) {
                creatureName = split[2];
            }
            if (split.Length >= 4) {
                rawName = split[3];
            }
            lootCreature = StorageManager.getCreature(creatureName);
            InitializeComponent();
            value_tooltip.AutoPopDelay = 60000;
            value_tooltip.InitialDelay = 500;
            value_tooltip.ReshowDelay = 0;
            value_tooltip.ShowAlways = true;
            value_tooltip.UseFading = true;
            this.Name = "Tibialyzer (Loot Form)";
            updateTimer = new System.Timers.Timer(500);
            updateTimer.AutoReset = false;
            updateTimer.Elapsed += (s, e) => {
                ActuallyRefreshForm();
            };
        }
        private void ActuallyRefreshForm() {
            lock (updateLock) {
                if (this.IsDisposed) return;
                try {
                    updateTimer.Stop();
                    updateTimer.Enabled = false;
                    this.Invoke((MethodInvoker)delegate {
                        UpdateLootInternal();
                    });
                } catch {

                }
            }
        }

        public static Bitmap GetStackImage(Image image, int count, Item item) {
            if (image == null) return new Bitmap(item.image);
            lock(image) {
                int max = image.GetFrameCount(FrameDimension.Time);
                int index = 0;

                if (count <= 5) index = count - 1;
                else if (count <= 10) index = 5;
                else if (count <= 25) index = 6;
                else if (count <= 50) index = 7;
                else index = 8;

                if (index >= max) index = max - 1;
                image.SelectActiveFrame(FrameDimension.Time, index);
                return new Bitmap((Image)image.Clone());
            }
        }

        public static void DrawCountOnGraphics(Graphics gr, int itemCount, int offset_x, int offset_y) {
            int numbers = (int)Math.Floor(Math.Log(itemCount, 10)) + 1;
            int xoffset = 1, logamount = itemCount;
            for (int i = 0; i < numbers; i++) {
                int imagenr = logamount % 10;
                Image imageNumber = StyleManager.GetImage(imagenr + ".png");
                xoffset = xoffset + imageNumber.Width + (itemCount >= 1000 ? 0 : 1);
                lock (imageNumber) {
                    gr.DrawImage(imageNumber, new Point(offset_x - xoffset, offset_y - imageNumber.Height - 3));
                }
                logamount /= 10;
            }
        }

        public static Bitmap DrawCountOnItem(Item item, int itemCount, int size = -1) {
            Bitmap image;
            if (item.stackable) {
                try {
                    image = new Bitmap(LootDropForm.GetStackImage(item.image, itemCount, item));
                } catch {
                    image = new Bitmap(item.image);
                }
            } else {
                image = new Bitmap(item.image);
            }
            
            using (Graphics gr = Graphics.FromImage(image)) {
                DrawCountOnGraphics(gr, itemCount, image.Width, image.Height);
            }
            return image;
        }

        public static Tuple<Dictionary<Creature, int>, List<Tuple<Item, int>>> GenerateLootInformation(Hunt hunt, string rawName, Creature lootCreature) {
            Dictionary<Creature, int> creatureKills;
            List<Tuple<Item, int>> itemDrops = new List<Tuple<Item, int>>();

            bool raw = rawName == "raw";
            bool all = raw || rawName == "all";
            List<Creature> displayedCreatures = null;
            if (!hunt.trackAllCreatures && hunt.trackedCreatures.Length > 0) {
                displayedCreatures = hunt.GetTrackedCreatures();
            } else if (SettingsManager.getSettingBool("IgnoreLowExperience")) {
                displayedCreatures = new List<Creature>();
                foreach (Creature cr in hunt.IterateCreatures()) {
                    if (cr.experience >= SettingsManager.getSettingInt("IgnoreLowExperienceValue")) {
                        displayedCreatures.Add(cr);
                    }
                }
            }

            if (lootCreature != null) {
                //the command is loot@<creature>, so we only display the kills and loot from the specified creature
                creatureKills = hunt.GetCreatureKills(lootCreature);
            } else if (displayedCreatures == null) {
                creatureKills = hunt.GetCreatureKills(); //display all creatures //loot.killCount;
            } else {
                // only display tracked creatures
                creatureKills = hunt.GetCreatureKills(displayedCreatures); // new Dictionary<Creature, int>();
            }

            // now handle item drops, gather a count for every item
            Dictionary<Item, int> itemCounts = new Dictionary<Item, int>();
            foreach (KeyValuePair<Creature, Dictionary<Item, int>> kvp in hunt.IterateLoot()) {
                if (lootCreature != null && kvp.Key != lootCreature) continue; // if lootCreature is specified, only consider loot from the specified creature
                if (displayedCreatures != null && !displayedCreatures.Contains(kvp.Key)) continue;
                foreach (KeyValuePair<Item, int> kvp2 in kvp.Value) {
                    Item item = kvp2.Key;
                    int value = kvp2.Value;
                    if (!itemCounts.ContainsKey(item)) itemCounts.Add(item, value);
                    else itemCounts[item] += value;
                }
            }

            // now we do item conversion
            long extraGold = 0;
            foreach (KeyValuePair<Item, int> kvp in itemCounts) {
                Item item = kvp.Key;
                int count = kvp.Value;
                // discard items that are set to be discarded (as long as all/raw mode is not enabled)
                if (item.discard && !all) continue;
                // convert items to gold (as long as raw mode is not enabled), always gather up all the gold coins found
                if ((!raw && item.convert_to_gold) || item.displayname == "gold coin" || item.displayname == "platinum coin" || item.displayname == "crystal coin") {
                    extraGold += item.GetMaxValue() * count;
                } else {
                    itemDrops.Add(new Tuple<Item, int>(item, count));
                }
            }

            // handle coin drops, we always convert the gold to the highest possible denomination (so if gold = 10K, we display a crystal coin)
            long currentGold = extraGold;
            if (currentGold > 10000) {
                itemDrops.Add(new Tuple<Item, int>(StorageManager.getItem("crystal coin"), (int)(currentGold / 10000)));
                currentGold = currentGold % 10000;
            }
            if (currentGold > 100) {
                itemDrops.Add(new Tuple<Item, int>(StorageManager.getItem("platinum coin"), (int)(currentGold / 100)));
                currentGold = currentGold % 100;
            }
            if (currentGold > 0) {
                itemDrops.Add(new Tuple<Item, int>(StorageManager.getItem("gold coin"), (int)(currentGold)));
            }

            // now order by value so most valuable items are placed first
            // we use a special value for the gold coins so the gold is placed together in the order crystal > platinum > gold
            // gold coins = <gold total> - 2, platinum coins = <gold total> - 1, crystal coins = <gold total>
            itemDrops = itemDrops.OrderByDescending(o => o.Item1.displayname == "gold coin" ? extraGold - 2 : (o.Item1.displayname == "platinum coin" ? extraGold - 1 : (o.Item1.displayname == "crystal coin" ? extraGold : o.Item1.GetMaxValue() * o.Item2))).ToList();
            return new Tuple<Dictionary<Creature, int>, List<Tuple<Item, int>>>(creatureKills, itemDrops);
        }

        private void UpdateLootInternal() {
            refreshTimer();
            var tpl = LootDropForm.GenerateLootInformation(hunt, rawName, lootCreature);
            creatures = tpl.Item1;
            items = tpl.Item2;
            this.SuspendForm();
            RefreshLoot();
            this.ResumeForm();
        }

        public void UpdateLoot() {
            if (this.IsDisposed) return;
            lock (updateLock) {
                if (!updateTimer.Enabled) {
                    updateTimer.Start();
                }
            }
        }

        public static string TimeToString(long totalSeconds) {
            string displayString = "";
            if (totalSeconds >= 3600) {
                displayString += (totalSeconds / 3600).ToString() + "h ";
                totalSeconds = totalSeconds % 3600;
            }
            if (totalSeconds >= 60) {
                displayString += (totalSeconds / 60).ToString() + "m ";
                totalSeconds = totalSeconds % 60;
            }
            displayString += totalSeconds.ToString() + "s";
            return displayString;
        }

        public static long GetAverageGold(Dictionary<Creature, int> creatures) {
            long averageGold = 0;
            foreach (KeyValuePair<Creature, int> tpl in creatures) {
                double average = 0;
                foreach (ItemDrop dr in tpl.Key.itemdrops) {
                    Item it = StorageManager.getItem(dr.itemid);
                    if (!it.discard && it.GetMaxValue() > 0 && dr.percentage > 0) {
                        average += ((dr.min + dr.max) / 2.0) * (dr.percentage / 100.0) * it.GetMaxValue();
                    }
                }
                averageGold += (int)(average * tpl.Value);
            }
            return averageGold;
        }

        public List<Control> createdControls = new List<Control>();
        public void RefreshLoot() {
            foreach (Control c in createdControls) {
                this.Controls.Remove(c);
                c.Dispose();
            }
            createdControls.Clear();
            if (page < 0) page = 0;

            int base_x = 20, base_y = 30;
            int x = 0, y = 0;
            int item_spacing = 4;
            Size item_size = new Size(32, 32);
            int max_x = SettingsManager.getSettingInt("LootFormWidth");
            if (max_x < minLootWidth) max_x = minLootWidth;
            int width_x = max_x + item_spacing * 2;

            long total_value = 0;
            int currentPage = 0;
            bool prevPage = page > 0;
            bool nextPage = false;

            averageGold = GetAverageGold(creatures);

            foreach (Tuple<Item, int> tpl in items) {
                total_value += tpl.Item1.GetMaxValue() * tpl.Item2;
            }
            Dictionary<Item, List<PictureBox>> newItemControls = new Dictionary<Item, List<PictureBox>>();
            foreach (Tuple<Item, int> tpl in items) {
                Item item = tpl.Item1;
                int count = tpl.Item2;
                while (count > 0) {
                    if (base_x + x >= (max_x - item_size.Width - item_spacing)) {
                        x = 0;
                        if (y + item_size.Height + item_spacing > pageHeight) {
                            currentPage++;
                            if (currentPage > page) {
                                nextPage = true;
                                break;
                            } else {
                                y = 0;
                            }
                        } else {
                            y = y + item_size.Height + item_spacing;
                        }
                    }
                    int mitems = 1;
                    if (item.stackable || SettingsManager.getSettingBool("StackAllItems")) mitems = Math.Min(count, 100);
                    count -= mitems;
                    if (currentPage == page) {
                        PictureBox picture_box;
                        if (itemControls.ContainsKey(item)) {
                            picture_box = itemControls[item][0];
                            itemControls[item].RemoveAt(0);
                            if (itemControls[item].Count == 0) {
                                itemControls.Remove(item);
                            }
                            picture_box.Location = new System.Drawing.Point(base_x + x, base_y + y);
                            if (picture_box.TabIndex != mitems && (item.stackable || mitems > 1)) {
                                picture_box.Image = LootDropForm.DrawCountOnItem(item, mitems);
                            }
                            picture_box.TabIndex = mitems;
                            long individualValue = item.GetMaxValue();
                            value_tooltip.SetToolTip(picture_box, System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(item.displayname) + " value: " + (individualValue >= 0 ? (individualValue * mitems).ToString() : "Unknown"));
                        } else {
                            picture_box = new PictureBox();
                            picture_box.Location = new System.Drawing.Point(base_x + x, base_y + y);
                            picture_box.Name = item.GetName();
                            picture_box.Size = new System.Drawing.Size(item_size.Width, item_size.Height);
                            picture_box.TabIndex = mitems;
                            picture_box.TabStop = false;
                            if (item.stackable || mitems > 1) {
                                picture_box.Image = LootDropForm.DrawCountOnItem(item, mitems);
                            } else {
                                picture_box.Image = item.GetImage();
                            }

                            picture_box.SizeMode = PictureBoxSizeMode.StretchImage;
                            picture_box.BackgroundImage = StyleManager.GetImage("item_background.png");
                            picture_box.Click += openItemBox;
                            long individualValue = item.GetMaxValue();
                            value_tooltip.SetToolTip(picture_box, System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(item.displayname) + " value: " + (individualValue >= 0 ? (individualValue * mitems).ToString() : "Unknown"));
                            this.Controls.Add(picture_box);
                        }
                        if (!newItemControls.ContainsKey(item)) newItemControls.Add(item, new List<PictureBox>());
                        newItemControls[item].Add(picture_box);
                    }

                    x += item_size.Width + item_spacing;
                }
                if (currentPage > page) {
                    break;
                }
            }
            if (page > currentPage) {
                page = currentPage;
                RefreshLoot();
                return;
            }

            foreach (KeyValuePair<Item, List<PictureBox>> kvp in itemControls) {
                foreach (PictureBox p in kvp.Value) {
                    this.Controls.Remove(p);
                    p.Dispose();
                }
            }
            itemControls = newItemControls;

            y = y + item_size.Height + item_spacing;
            if (prevPage) {
                PictureBox prevpage = new PictureBox();
                prevpage.Location = new Point(10, base_y + y);
                prevpage.Size = new Size(97, 23);
                prevpage.Image = StyleManager.GetImage("prevpage.png");
                prevpage.BackColor = Color.Transparent;
                prevpage.SizeMode = PictureBoxSizeMode.StretchImage;
                prevpage.Click += Prevpage_Click;
                this.Controls.Add(prevpage);
                createdControls.Add(prevpage);
            }
            if (nextPage) {
                PictureBox nextpage = new PictureBox();
                nextpage.Location = new Point(width_x - 108, base_y + y);
                nextpage.Size = new Size(98, 23);
                nextpage.BackColor = Color.Transparent;
                nextpage.Image = StyleManager.GetImage("nextpage.png");
                nextpage.SizeMode = PictureBoxSizeMode.StretchImage;
                nextpage.Click += Nextpage_Click;
                this.Controls.Add(nextpage);
                createdControls.Add(nextpage);
            }
            if (prevPage || nextPage) y += 23;

            x = 0;
            base_x = 5;
            Size creature_size = new Size(1, 1);
            Size labelSize = new Size(1, 1);

            foreach (KeyValuePair<Creature, int> tpl in creatures) {
                Creature creature = tpl.Key;
                creature_size.Width = Math.Max(creature_size.Width, creature.GetImage().Width);
                creature_size.Height = Math.Max(creature_size.Height, creature.GetImage().Height);
            }
            {
                Dictionary<Creature, Tuple<PictureBox, Label>> newCreatureControls = new Dictionary<Creature, Tuple<PictureBox, Label>>();
                int i = 0;
                foreach (Creature cr in creatures.Keys.OrderByDescending(o => creatures[o] * (1 + o.experience)).ToList<Creature>()) {
                    Creature creature = cr;
                    int killCount = creatures[cr];
                    if (x >= max_x - creature_size.Width - item_spacing * 2) {
                        x = 0;
                        y = y + creature_size.Height + 23;
                        if (y > maxCreatureHeight) {
                            break;
                        }
                    }
                    int xoffset = (creature_size.Width - creature.GetImage().Width) / 2;
                    int yoffset = (creature_size.Height - creature.GetImage().Height) / 2;

                    Label count;
                    PictureBox picture_box;
                    if (creatureControls.ContainsKey(creature)) {
                        picture_box = creatureControls[creature].Item1;
                        count = creatureControls[creature].Item2;
                        creatureControls.Remove(creature);

                        picture_box.Location = new System.Drawing.Point(base_x + x + xoffset, base_y + y + yoffset + (creature_size.Height - creature.GetImage().Height) / 2);
                        count.Location = new Point(base_x + x + xoffset, base_y + y + creature_size.Height);
                        count.Text = killCount.ToString() + "x";
                    } else {
                        count = new Label();
                        count.Text = killCount.ToString() + "x";
                        count.Font = loot_font;
                        count.Size = new Size(1, 10);
                        count.Location = new Point(base_x + x + xoffset, base_y + y + creature_size.Height);
                        count.AutoSize = true;
                        count.TextAlign = ContentAlignment.MiddleCenter;
                        count.ForeColor = StyleManager.NotificationTextColor;
                        count.BackColor = Color.Transparent;

                        picture_box = new PictureBox();
                        picture_box.Location = new System.Drawing.Point(base_x + x + xoffset, base_y + y + yoffset + (creature_size.Height - creature.GetImage().Height) / 2);
                        picture_box.Name = creature.GetName();
                        picture_box.Size = new System.Drawing.Size(creature.GetImage().Width, creature.GetImage().Height);
                        picture_box.TabIndex = 1;
                        picture_box.TabStop = false;
                        picture_box.Image = creature.GetImage();
                        picture_box.SizeMode = PictureBoxSizeMode.StretchImage;
                        picture_box.Click += openCreatureDrops;
                        picture_box.BackColor = Color.Transparent;

                        this.Controls.Add(picture_box);
                        this.Controls.Add(count);
                    }
                    int measured_size = (int)count.CreateGraphics().MeasureString(count.Text, count.Font).Width;
                    int width = Math.Max(measured_size, creature.GetImage().Width);

                    if (width > creature.GetImage().Width) {
                        picture_box.Location = new Point(picture_box.Location.X + (width - creature.GetImage().Width) / 2, picture_box.Location.Y);
                    } else {
                        count.Location = new Point(count.Location.X + (width - measured_size) / 2, count.Location.Y);
                    }
                    newCreatureControls.Add(creature, new Tuple<PictureBox, Label>(picture_box, count));

                    labelSize = count.Size;

                    i++;
                    x += width + xoffset;
                }
                y = y + creature_size.Height + labelSize.Height * 2;
                foreach (KeyValuePair<Creature, Tuple<PictureBox, Label>> kvp in creatureControls) {
                    this.Controls.Remove(kvp.Value.Item1);
                    this.Controls.Remove(kvp.Value.Item2);
                    kvp.Value.Item1.Dispose();
                    kvp.Value.Item2.Dispose();
                }
                creatureControls = newCreatureControls;
            }

            long usedItemValue = 0;
            foreach (var tpl in HuntManager.GetUsedItems(hunt)) {
                usedItemValue += tpl.Item1.GetMaxValue() * tpl.Item2;
            }

            int xPosition = width_x - totalValueValue.Size.Width - 5;
            y = base_y + y + item_spacing + 10;
            huntNameLabel.Text = hunt.name.ToString();
            totalValueLabel.Location = new Point(5, y);
            totalValueValue.Location = new Point(xPosition, y);
            totalValueValue.Text = total_value.ToString("N0");
            value_tooltip.SetToolTip(totalValueValue, String.Format("Average gold for these creature kills: {0} gold.", averageGold.ToString("N0")));
            totalExpLabel.Location = new Point(5, y += 20);
            totalExpValue.Location = new Point(xPosition, y);
            totalExpValue.Text = hunt.totalExp.ToString("N0");
            expHourValue.Text = ScanningManager.lastResults == null ? "-" : ScanningManager.lastResults.expPerHour.ToString("N0");
            expHourLabel.Location = new Point(5, y += 20);
            expHourValue.Location = new Point(xPosition, y);
            totalTimeLabel.Location = new Point(5, y += 20);
            totalTimeValue.Location = new Point(xPosition, y);
            usedItemsValue.Text = usedItemValue.ToString("N0");
            usedItemsLabel.Location = new Point(5, y += 20);
            usedItemsValue.Location = new Point(xPosition, y);
            long profit = total_value - usedItemValue;
            value_tooltip.SetToolTip(usedItemsValue, String.Format(profit > 0 ? "Total Profit: {0} gold" : "Total Waste: {0} gold", profit.ToString("N0")));

            totalTimeValue.Text = TimeToString((long)hunt.totalTime);
            y += 20;


            int widthSize = width_x / 3 - 5;
            lootButton.Size = new Size(widthSize, lootButton.Size.Height);
            lootButton.Location = new Point(5, y);
            allLootButton.Size = new Size(widthSize, lootButton.Size.Height);
            allLootButton.Location = new Point(7 + widthSize, y);
            rawLootButton.Size = new Size(widthSize, lootButton.Size.Height);
            rawLootButton.Location = new Point(10 + 2 * widthSize, y);

            y += allLootButton.Size.Height + 2;

            huntNameLabel.Size = new Size(width_x, huntNameLabel.Size.Height);
            this.Size = new Size(width_x, y + 5);
            lootLarger.Location = new Point(Size.Width - lootLarger.Size.Width - 4, 4);
            lootSmaller.Location = new Point(Size.Width - 2 * lootLarger.Size.Width - 4, 4);
        }

        public override void LoadForm() {
            this.NotificationInitialize();

            UnregisterControl(lootSmaller);
            UnregisterControl(lootLarger);
            UnregisterControl(rawLootButton);
            UnregisterControl(allLootButton);
            UnregisterControl(lootButton);

            UpdateLootInternal();

            base.NotificationFinalize();
        }

        private void Prevpage_Click(object sender, EventArgs e) {
            page--;
            this.SuspendForm();
            this.RefreshLoot();
            this.ResumeForm();
            this.Refresh();
            this.refreshTimer();
        }

        private void Nextpage_Click(object sender, EventArgs e) {
            page++;
            this.SuspendForm();
            this.RefreshLoot();
            this.ResumeForm();
            this.Refresh();
            this.refreshTimer();
        }

        void openItemBox(object sender, EventArgs e) {
            this.ReturnFocusToTibia();
            CommandManager.ExecuteCommand("item" + Constants.CommandSymbol + (sender as Control).Name);
        }

        void openCreatureDrops(object sender, EventArgs e) {
            if (creatures.Keys.Count == 1) {
                CommandManager.ExecuteCommand("creature" + Constants.CommandSymbol + (sender as Control).Name);
            } else {
                CommandManager.ExecuteCommand(String.Format("loot{0}{1}{0}{2}{0}{3}", Constants.CommandSymbol, huntName, (sender as Control).Name, rawName));
            }
        }

        private void huntNameLabel_Click(object sender, EventArgs e) {
        }

        private void rawLootButton_Click(object sender, EventArgs e) {
            rawName = "raw";
            this.UpdateLootInternal();
            this.UpdateCommand();
        }

        private void allLootButton_Click(object sender, EventArgs e) {
            rawName = "all";
            this.UpdateLootInternal();
            this.UpdateCommand();
        }

        private void lootButton_Click(object sender, EventArgs e) {
            rawName = "";
            creatureName = "";
            lootCreature = null;
            this.UpdateLootInternal();
            this.UpdateCommand();
        }

        private void UpdateCommand() {
            this.command.command = String.Format("loot{0}{1}{0}{2}{0}{3}", Constants.CommandSymbol, huntName, lootCreature == null ? "" : lootCreature.GetName(), rawName);
        }

        private void changeSize(int modification) {
            int max_x = SettingsManager.getSettingInt("LootFormWidth");
            if (max_x < minLootWidth) max_x = minLootWidth;
            max_x += modification;
            if (max_x < minLootWidth) max_x = minLootWidth;
            SettingsManager.setSetting("LootFormWidth", (max_x).ToString());
            this.SuspendForm();
            this.RefreshLoot();
            this.ResumeForm();
            this.Refresh();
            this.refreshTimer();
        }

        private void lootSmaller_Click(object sender, EventArgs e) {
            changeSize(-36);
        }

        private void lootLarger_Click(object sender, EventArgs e) {
            changeSize(36);
        }

        public override string FormName() {
            return "LootDropForm";
        }
    }
}
