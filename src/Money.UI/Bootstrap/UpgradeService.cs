﻿using Microsoft.EntityFrameworkCore;
using Money.Data;
using Money.Services;
using Money.Services.Models.Builders;
using Neptuo;
using Neptuo.Data;
using Neptuo.Events;
using Neptuo.Formatters;
using Neptuo.Migrations;
using Neptuo.ReadModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;

namespace Money.Bootstrap
{
    internal class UpgradeService : IUpgradeService
    {
        private readonly IDomainFacade domainFacade;
        private readonly IEventRebuilderStore eventStore;
        private readonly IFormatter eventFormatter;

        public const int CurrentVersion = 4;

        public UpgradeService(IDomainFacade domainFacade, IEventRebuilderStore eventStore, IFormatter eventFormatter)
        {
            Ensure.NotNull(domainFacade, "domainFacade");
            Ensure.NotNull(eventStore, "eventStore");
            Ensure.NotNull(eventFormatter, "eventFormatter");
            this.domainFacade = domainFacade;
            this.eventStore = eventStore;
            this.eventFormatter = eventFormatter;
        }

        public bool IsRequired()
        {
            return CurrentVersion > GetCurrentVersion();
        }

        public async Task UpgradeAsync(IUpgradeContext context)
        {
            int currentVersion = GetCurrentVersion();
            if (CurrentVersion <= currentVersion)
                return;

            context.TotalSteps(CurrentVersion - currentVersion);
            await Task.Delay(500);

            if (currentVersion < 1)
            {
                context.StartingStep(0 - currentVersion, "Creating default categories.");
                await UpgradeVersion1();
            }

            if (currentVersion < 2)
            {
                context.StartingStep(1 - currentVersion, "Rebuilding internal database.");
                await UpgradeVersion2();
            }

            if (currentVersion < 3)
            {
                context.StartingStep(2 - currentVersion, "Creating default currencies.");
                await UpgradeVersion3();
            }

            if (currentVersion < 4)
            {
                context.StartingStep(3 - currentVersion, "Adding support for category icons.");
                await UpgradeVersion4();
            }

            ApplicationDataContainer migrationContainer = GetMigrationContainer();
            migrationContainer.Values["Version"] = CurrentVersion;
        }

        private ApplicationDataContainer GetMigrationContainer()
        {
            ApplicationDataContainer root = ApplicationData.Current.LocalSettings;

            ApplicationDataContainer migrationContainer;
            if (!root.Containers.TryGetValue("Migration", out migrationContainer))
                migrationContainer = root.CreateContainer("Migration", ApplicationDataCreateDisposition.Always);

            return migrationContainer;
        }

        private int GetCurrentVersion()
        {
            ApplicationDataContainer migrationContainer = GetMigrationContainer();
            int currentVersion = (int?)migrationContainer.Values["Version"] ?? 0;
            return currentVersion;
        }

        private async Task UpgradeVersion1()
        {
            EventSourcingContext();
            await RecreateReadModelContextAsync();

            await domainFacade.CreateCategoryAsync("Home", Colors.SandyBrown);
            await domainFacade.CreateCategoryAsync("Food", Colors.OrangeRed);
            await domainFacade.CreateCategoryAsync("Eating Out", Colors.DarkRed);
        }

        private Task UpgradeVersion2()
        {
            return RecreateReadModelContextAsync();
        }

        private async Task UpgradeVersion3()
        {
            await RecreateReadModelContextAsync();
            await domainFacade.CreateCurrencyAsync("CZK", "Kč");
        }

        private void EventSourcingContext()
        {
            using (var eventSourcing = new EventSourcingContext())
            {
                eventSourcing.Database.EnsureDeleted();
                eventSourcing.Database.EnsureCreated();
                eventSourcing.Database.Migrate();
            }
        }

        internal Task RecreateReadModelContextAsync()
        {
            using (var readModels = new ReadModelContext())
            {
                readModels.Database.EnsureDeleted();
                readModels.Database.EnsureCreated();
            }

            // Should match with ReadModels.
            Rebuilder rebuilder = new Rebuilder(eventStore, eventFormatter);
            rebuilder.AddAll(new CategoryBuilder());
            rebuilder.AddAll(new OutcomeBuilder(domainFacade.PriceFactory));
            rebuilder.AddAll(new CurrencyBuilder());
            return rebuilder.RunAsync();
        }

        private async Task UpgradeVersion4()
        {
            await RecreateReadModelContextAsync();
        }
    }
}
