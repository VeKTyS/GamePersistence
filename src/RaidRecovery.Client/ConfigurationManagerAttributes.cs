using System;
using BepInEx.Configuration;

namespace RaidRecovery.Client
{
    /// <summary>
    /// Display hints for the BepInEx configuration menu (F12). The menu finds this class by its name and reads
    /// its fields by reflection: the class and field names must not change. Only the fields we use are declared.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        /// <summary>Draws the setting ourselves instead of the default editor.</summary>
        public Action<ConfigEntryBase> CustomDrawer;

        /// <summary>Hides the "Reset" button, meaningless for a setting that holds no value.</summary>
        public bool? HideDefaultButton;

        /// <summary>Settings with a higher order come first within their section.</summary>
        public int? Order;
    }
}
