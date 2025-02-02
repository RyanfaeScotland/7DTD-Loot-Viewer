﻿using ConfigParsers.Common;

namespace ConfigParsers.Loot.Data
{
    /// <summary>
    /// Parses the Raw XML classes, and attempts to build a more useful data structure from it...
    /// ... where all items refer to other items, allowing you to walk the tree
    /// </summary>
    public class DataStore
    {
        /// <summary>
        /// Loot Groups. Essentially the contents of all lootgroup nodes.
        /// Note however that one loot group can refer to another loot group
        /// </summary>
        public SortedDictionary<string, Group> Groups { get; } = new SortedDictionary<string, Group>();

        /// <summary>
        /// Probability Templates
        /// </summary>
        public SortedDictionary<string, ProbTemplate> Templates { get; } = new SortedDictionary<string, ProbTemplate>();

        /// <summary>
        /// Containers
        /// </summary>
        public SortedDictionary<string, Group> Containers { get; } = new SortedDictionary<string, Group> ();

        /// <summary>
        /// Items
        /// </summary>
        public SortedDictionary<string, Item>  Items { get; } = new SortedDictionary<string, Item> ();

        /// <summary>
        /// Data table is built upon instantiation of the class
        /// </summary>
        /// <param name="rawRoot">The deserialized XML document</param>
        public DataStore(XmlClasses.Root rawRoot)
        {
            // Iterate through all lootprobtemplate nodes in the XML
            // We do this before iterating groups, as groups reference probability templates
            if (rawRoot.LootProbTemplateBase.Count() > 0)
            {
                var probTemplates = rawRoot.LootProbTemplateBase[0].LootProbTemplates;
                foreach (var template in probTemplates)
                {
                    Templates.Add(template.Name, new ProbTemplate(template));
                }
            }

            // Iterate through all lootqualitytemplate nodes in the XML
            // Some Items (eg ammo9mmBulletBall in groupAmmoRegularGunslingerT1) use a Quality Template as a Probability Template...
            // ... so even if not trying to calculate Quality, we need to add the Quality Templates to the Probability Template list
            foreach (var qualTemplateBase in rawRoot.LootQualTemplateBase)
            {
                foreach (var template in qualTemplateBase.LootQualTemplates)
                {
                    Templates.Add(template.Name, new ProbTemplate(template));
                }

            }

            // Iterate through all lootgroup nodes
            // At this point, only add Groups, and not Group References
            foreach (var rawGroup in rawRoot.Groups)
            {
                var group = new Group(rawGroup.Name, new Count(rawGroup.Count), GroupType.Group);
                Groups.Add(rawGroup.Name, group);
            }

            // Iterate through all container nodes
            // Add Groups to containers
            foreach (var rawGroup in rawRoot.Containers)
            {
                if (rawRoot.IsIgnoredContainer(rawGroup.Name)) continue;
                var group = new Group(rawGroup.Name, new Count(rawGroup.Count), GroupType.Container);
                Groups.Add(rawGroup.Name, group);
                Containers.Add(rawGroup.Name, group);
                BuildGroup(rawGroup.Name, rawRoot.GroupsDictionary);
            }
        }

        private void BuildGroup(string groupName, Dictionary<string, XmlClasses.Group> rawGroups)
        {
            var rawGroup = rawGroups[groupName];
            var group = Groups[groupName];

            // If we have already built this group, then do not build it again
            if (group.Items.Count > 0 || group.GroupReferences.Count > 0) return;

            // Iterate through all Entries in the Group
            for (int i = 0; i < rawGroup.Entries.Count(); i++)
            {
                var rawEntry = rawGroup.Entries[i];
                if (!string.IsNullOrEmpty(rawEntry.Name))
                {
                    // Entry is an Item
                    if (group.Items.ContainsKey(rawEntry.Name))
                    {
                        // ToDo: ammo9mmBulletBall appears twice in groupAmmoRegularGunslingerT1...
                        // ... once with ProbTemplate QLTemplateT0 and once with QLTemplateT1
                        // Until I know how to deal with this, skip subsequent entries
                        continue;
                    }

                    try
                    {
                        var itemInstance = AddItem(rawEntry, group, group.Items.Count);
                        group.Items.Add(rawEntry.Name, itemInstance);
                    }
                    catch (KeyNotFoundException)
                    {
                        // ToDo: ammo9mmBulletBall in groupAmmoAdvancedGunslinger references Probability Template "T0", which does not exist
                        // Until I know if this is an error in the XML or not, I will have to throw an exception and catch it
                        continue;
                    }

                }
                else if (!string.IsNullOrEmpty(rawEntry.Group))
                {
                    // Entry is a Group (Which could contain other groups)
                    var childGroup = Groups[rawEntry.Group];
                    // Build a reference that links this group and the childGroup
                    var childGroupReference = new GroupReference(
                        group: childGroup,
                        parent: group,
                        parentGroupReferenceIndex: group.GroupReferences.Count,
                        count: new Count(rawEntry.Count),
                        prob: string.IsNullOrEmpty(rawEntry.Prob) ? null : Convert.ToDecimal(rawEntry.Prob),
                        probTemplate: string.IsNullOrEmpty(rawEntry.ProbTemplate) ? null : Templates[rawEntry.ProbTemplate],
                        forceProb: string.IsNullOrEmpty(rawEntry.ForceProb) ? null : Convert.ToBoolean(rawEntry.ForceProb)
                    );
                    // Add the reference to the child to this Group
                    group.GroupReferences.Add(childGroupReference);
                    // Add the reference to this Group to the child
                    childGroup.ParentGroupReferences.Add(childGroupReference);
                    // Build the Items and Groups in the child
                    BuildGroup(childGroup.Name, rawGroups);
                }
                else
                {
                    throw new FormatException($"Loot Group entry {i} has neither a Name nor Group");
                }

            }
        }

        /// <summary>
        /// Adds an item to the list of Items
        /// </summary>
        /// <param name="rawEntry">The XML for the Item</param>
        /// <param name="group">The Group that the Item is being added to</param>
        /// <param name="instanceIndex">The index that the item will have in the Group's Items dictionary</param>
        /// </summary>
        /// <returns>An ItemInstance object that refers to the instance of this Item</returns>

        /// <returns></returns>
        private ItemInstance AddItem(XmlClasses.Item rawEntry, Group group, int instanceIndex)
        {
            Item item;
            if (Items.ContainsKey(rawEntry.Name))
            {
                // Item already exists in the database, just add an instance of it
                item = Items[rawEntry.Name];
            }
            else
            {
                // First time this Item has been seen
                item = new Item(rawEntry.Name);
                Items.Add(rawEntry.Name, item);
            }

            var itemInstance = item.AddInstance(rawEntry, Templates, group, instanceIndex);
            return itemInstance;
        }
    }
}
