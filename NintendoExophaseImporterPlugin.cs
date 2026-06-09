using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using NintendoExophaseImporter.Exophase;
using NintendoExophaseImporter.Settings;
using NintendoExophaseImporter.Sync;

namespace NintendoExophaseImporter
{
    public class NintendoExophaseImporterPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public NintendoExophaseSettingsViewModel SettingsViewModel { get; }

        public override Guid Id { get; } = Guid.Parse("1f9cc97c-a416-4b2e-b28d-a00fe5048404");

        public NintendoExophaseImporterPlugin(IPlayniteAPI api) : base(api)
        {
            SettingsViewModel = new NintendoExophaseSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => SettingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new NintendoExophaseSettingsView();

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Sync Nintendo Switch playtime from Exophase",
                MenuSection = "@Nintendo Exophase Importer",
                Action = _ => RunSync()
            };
        }

        private void RunSync()
        {
            if (!SettingsViewModel.VerifySettings(out var errors))
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Join("\n", errors), "Nintendo Exophase Importer");
                OpenSettingsView();
                return;
            }

            SyncResult syncResult = null;
            var service = new SyncService(PlayniteApi, this, logger);

            var progressResult = PlayniteApi.Dialogs.ActivateGlobalProgress(
                a => { syncResult = service.Sync(text => a.Text = text, a.CancelToken); },
                new GlobalProgressOptions("Syncing Nintendo Switch playtime (Exophase)…", cancelable: true)
                {
                    IsIndeterminate = true
                });

            if (progressResult.Canceled)
            {
                return;
            }

            if (progressResult.Error != null)
            {
                logger.Error(progressResult.Error, "Nintendo Switch / Exophase sync failed.");
                var message = progressResult.Error is ExophaseException
                    ? progressResult.Error.Message
                    : "Sync failed: " + progressResult.Error.Message;
                PlayniteApi.Dialogs.ShowErrorMessage(message, "Nintendo Exophase Importer");
                return;
            }

            PlayniteApi.Dialogs.ShowMessage(
                syncResult?.ToString() ?? "No result.",
                "Nintendo Exophase Importer");
        }
    }
}
