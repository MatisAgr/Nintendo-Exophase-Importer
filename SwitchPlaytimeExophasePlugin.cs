using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using SwitchPlaytimeExophase.Exophase;
using SwitchPlaytimeExophase.Settings;
using SwitchPlaytimeExophase.Sync;

namespace SwitchPlaytimeExophase
{
    public class SwitchPlaytimeExophasePlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public SwitchPlaytimeSettingsViewModel SettingsViewModel { get; }

        public override Guid Id { get; } = Guid.Parse("1f9cc97c-a416-4b2e-b28d-a00fe5048404");

        public SwitchPlaytimeExophasePlugin(IPlayniteAPI api) : base(api)
        {
            SettingsViewModel = new SwitchPlaytimeSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => SettingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new SwitchPlaytimeSettingsView();

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Sync Switch playtime from Exophase",
                MenuSection = "@Switch Playtime (Exophase)",
                Action = _ => RunSync()
            };
        }

        private void RunSync()
        {
            if (!SettingsViewModel.VerifySettings(out var errors))
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Join("\n", errors), "Switch Playtime (Exophase)");
                OpenSettingsView();
                return;
            }

            SyncResult syncResult = null;
            var service = new SyncService(PlayniteApi, this, logger);

            var progressResult = PlayniteApi.Dialogs.ActivateGlobalProgress(
                a => { syncResult = service.Sync(text => a.Text = text, a.CancelToken); },
                new GlobalProgressOptions("Syncing Switch playtime (Exophase)…", cancelable: true)
                {
                    IsIndeterminate = true
                });

            if (progressResult.Canceled)
            {
                return;
            }

            if (progressResult.Error != null)
            {
                logger.Error(progressResult.Error, "Switch/Exophase sync failed.");
                var message = progressResult.Error is ExophaseException
                    ? progressResult.Error.Message
                    : "Sync failed: " + progressResult.Error.Message;
                PlayniteApi.Dialogs.ShowErrorMessage(message, "Switch Playtime (Exophase)");
                return;
            }

            PlayniteApi.Dialogs.ShowMessage(
                syncResult?.ToString() ?? "No result.",
                "Switch Playtime (Exophase)");
        }
    }
}
