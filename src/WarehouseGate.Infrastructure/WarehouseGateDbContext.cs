using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;

namespace WarehouseGate.Infrastructure;

public class WarehouseGateDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentTenantProvider _tenant;

    public WarehouseGateDbContext(DbContextOptions<WarehouseGateDbContext> options, ICurrentTenantProvider tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleMaster> VehicleMasters => Set<VehicleMaster>();
    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();
    public DbSet<VehicleCategory> VehicleCategories => Set<VehicleCategory>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<InwardTransaction> InwardTransactions => Set<InwardTransaction>();
    public DbSet<PhotoEvidence> PhotoEvidences => Set<PhotoEvidence>();
    public DbSet<InwardDocument> InwardDocuments => Set<InwardDocument>();
    public DbSet<InspectionLine> InspectionLines => Set<InspectionLine>();
    public DbSet<UnplannedReceiptLine> UnplannedReceiptLines => Set<UnplannedReceiptLine>();
    public DbSet<GoodsReceiptNote> GoodsReceiptNotes => Set<GoodsReceiptNote>();

    public DbSet<DispatchOrder> DispatchOrders => Set<DispatchOrder>();
    public DbSet<DispatchOrderLine> DispatchOrderLines => Set<DispatchOrderLine>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OutwardTransaction> OutwardTransactions => Set<OutwardTransaction>();
    public DbSet<OutwardPhotoEvidence> OutwardPhotoEvidences => Set<OutwardPhotoEvidence>();
    public DbSet<OutwardGateArrival> OutwardGateArrivals => Set<OutwardGateArrival>();
    public DbSet<OutwardGateArrivalPhoto> OutwardGateArrivalPhotos => Set<OutwardGateArrivalPhoto>();
    public DbSet<OutwardLoadLine> OutwardLoadLines => Set<OutwardLoadLine>();
    public DbSet<OutwardDispatchNote> OutwardDispatchNotes => Set<OutwardDispatchNote>();
    public DbSet<OutwardLoadPlanOption> OutwardLoadPlanOptions => Set<OutwardLoadPlanOption>();
    public DbSet<OutwardLoadPlanGroup> OutwardLoadPlanGroups => Set<OutwardLoadPlanGroup>();

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<State> States => Set<State>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Transporter> Transporters => Set<Transporter>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<VehicleLogisticsRecord> VehicleLogisticsRecords => Set<VehicleLogisticsRecord>();
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();
    public DbSet<DockBay> DockBays => Set<DockBay>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Vehicle>()
            .HasIndex(v => new { v.OrganizationId, v.Number })
            .IsUnique();

        builder.Entity<VehicleMaster>()
            .HasOne(v => v.VehicleType)
            .WithMany()
            .HasForeignKey(v => v.VehicleTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VehicleMaster>()
            .HasOne(v => v.VehicleCategory)
            .WithMany()
            .HasForeignKey(v => v.VehicleCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VehicleMaster>()
            .HasIndex(v => new { v.OrganizationId, v.VehicleTypeId, v.VehicleCategoryId })
            .IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasIndex(p => new { p.OrganizationId, p.PONumber })
            .IsUnique();

        builder.Entity<InwardTransaction>()
            .HasIndex(t => new { t.OrganizationId, t.InwardTxnNumber })
            .IsUnique();

        builder.Entity<InwardTransaction>()
            .HasOne(t => t.Vehicle)
            .WithMany()
            .HasForeignKey(t => t.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InwardTransaction>()
            .HasOne(t => t.PurchaseOrder)
            .WithMany()
            .HasForeignKey(t => t.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InwardTransaction>()
            .HasOne(t => t.Grn)
            .WithOne(g => g.InwardTransaction!)
            .HasForeignKey<GoodsReceiptNote>(g => g.InwardTransactionId);

        builder.Entity<InwardTransaction>()
            .Property(t => t.Status)
            .HasConversion<string>();

        builder.Entity<PhotoEvidence>()
            .Property(p => p.Type)
            .HasConversion<string>();

        builder.Entity<InwardDocument>()
            .Property(d => d.Type)
            .HasConversion<string>();

        builder.Entity<InspectionLine>()
            .Property(i => i.Condition)
            .HasConversion<string>();

        builder.Entity<PurchaseOrderLine>()
            .Property(l => l.ExpectedQty)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .Property(l => l.PickListQty)
            .HasPrecision(18, 2);

        builder.Entity<PurchaseOrderLine>()
            .Property(l => l.LoadedQty)
            .HasPrecision(18, 2);

        builder.Entity<InspectionLine>()
            .Property(l => l.ReceivedQty)
            .HasPrecision(18, 2);

        builder.Entity<PhotoEvidence>()
            .HasOne(p => p.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(p => p.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        // InwardTransaction <-> UnplannedReceiptLine is left to convention (matching
        // InwardTransaction.UnplannedLines / UnplannedReceiptLine.InwardTransaction, same as
        // Photos/Documents/InspectionLines above) - an explicit HasOne/WithMany() here without
        // naming the collection nav created a second, conflicting relationship (EF fell back to a
        // shadow "InwardTransactionId1" FK instead of reusing the real InwardTransactionId column).

        builder.Entity<UnplannedReceiptLine>()
            .HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UnplannedReceiptLine>()
            .Property(l => l.Quantity)
            .HasPrecision(18, 2);

        builder.Entity<DispatchOrder>()
            .HasIndex(d => new { d.OrganizationId, d.DispatchOrderNumber })
            .IsUnique();

        builder.Entity<OutwardTransaction>()
            .HasIndex(t => new { t.OrganizationId, t.OutwardTxnNumber })
            .IsUnique();

        builder.Entity<OutwardTransaction>()
            .HasOne(t => t.Vehicle)
            .WithMany()
            .HasForeignKey(t => t.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardTransaction>()
            .HasOne(t => t.DispatchOrder)
            .WithMany()
            .HasForeignKey(t => t.DispatchOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardTransaction>()
            .HasOne(t => t.DispatchNote)
            .WithOne(n => n.OutwardTransaction!)
            .HasForeignKey<OutwardDispatchNote>(n => n.OutwardTransactionId);

        builder.Entity<OutwardTransaction>()
            .Property(t => t.Status)
            .HasConversion<string>();

        builder.Entity<OutwardTransaction>()
            .Property(t => t.ExceptionReason)
            .HasConversion<string>();

        builder.Entity<OutwardPhotoEvidence>()
            .Property(p => p.Type)
            .HasConversion<string>();

        builder.Entity<OutwardGateArrival>()
            .HasOne(a => a.Vehicle)
            .WithMany()
            .HasForeignKey(a => a.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardGateArrival>()
            .HasOne(a => a.LinkedOutwardTransaction)
            .WithMany()
            .HasForeignKey(a => a.LinkedOutwardTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardGateArrivalPhoto>()
            .Property(p => p.Type)
            .HasConversion<string>();

        builder.Entity<DispatchOrderLine>()
            .Property(l => l.OrderedQty)
            .HasPrecision(18, 2);

        builder.Entity<OutwardLoadLine>()
            .Property(l => l.LoadedQty)
            .HasPrecision(18, 2);

        // Restrict (not Cascade) even though OutwardTransactions are never deleted in
        // this app: SQL Server rejects a second cascade path down to
        // OutwardPhotoEvidences (which OutwardTransaction already cascades to
        // directly) as "multiple cascade paths".
        builder.Entity<OutwardLoadPlanOption>()
            .HasOne(o => o.OutwardTransaction)
            .WithMany(t => t.LoadPlanOptions)
            .HasForeignKey(o => o.OutwardTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardLoadPlanGroup>()
            .HasOne(g => g.OutwardLoadPlanOption)
            .WithMany(o => o.Groups)
            .HasForeignKey(g => g.OutwardLoadPlanOptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OutwardLoadPlanGroup>()
            .HasOne(g => g.DispatchOrderLine)
            .WithMany()
            .HasForeignKey(g => g.DispatchOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardPhotoEvidence>()
            .HasOne(p => p.OutwardLoadPlanGroup)
            .WithMany(g => g.Photos)
            .HasForeignKey(p => p.OutwardLoadPlanGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<OutwardPhotoEvidence>()
            .HasOne(p => p.DispatchOrderLine)
            .WithMany()
            .HasForeignKey(p => p.DispatchOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.ZoneLength)
            .HasConversion<string>();
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.ZoneWidth)
            .HasConversion<string>();
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.ZoneHeight)
            .HasConversion<string>();
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.Orientation)
            .HasConversion<string>();
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.ConfirmationStatus)
            .HasConversion<string>();

        builder.Entity<FollowUpTask>()
            .Property(t => t.Type)
            .HasConversion<string>();
        builder.Entity<FollowUpTask>()
            .Property(t => t.Status)
            .HasConversion<string>();
        builder.Entity<FollowUpTask>()
            .HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => t.WarehouseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<DockBay>()
            .HasOne(b => b.Warehouse)
            .WithMany()
            .HasForeignKey(b => b.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);
        // One bay name per warehouse - "Bay-1" can exist at Mumbai AND Bengaluru, not twice at one.
        builder.Entity<DockBay>()
            .HasIndex(b => new { b.WarehouseId, b.Name })
            .IsUnique();
        builder.Entity<Warehouse>()
            .Property(w => w.DockOperatingHoursPerDay)
            .HasPrecision(5, 2);
        builder.Entity<Warehouse>()
            .Property(w => w.ShiftHoursPerDay)
            .HasPrecision(5, 2);

        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.PositionXCm)
            .HasPrecision(18, 2);
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.PositionYCm)
            .HasPrecision(18, 2);
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.PositionZCm)
            .HasPrecision(18, 2);
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.DimXCm)
            .HasPrecision(18, 2);
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.DimYCm)
            .HasPrecision(18, 2);
        builder.Entity<OutwardLoadPlanGroup>()
            .Property(g => g.DimZCm)
            .HasPrecision(18, 2);

        builder.Entity<DispatchOrderLine>()
            .HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Product>()
            .Property(p => p.WeightKg)
            .HasPrecision(18, 2);
        builder.Entity<Product>()
            .Property(p => p.LengthCm)
            .HasPrecision(18, 2);
        builder.Entity<Product>()
            .Property(p => p.WidthCm)
            .HasPrecision(18, 2);
        builder.Entity<Product>()
            .Property(p => p.HeightCm)
            .HasPrecision(18, 2);

        builder.Entity<Vehicle>()
            .Property(v => v.MaxWeightKg)
            .HasPrecision(18, 2);
        builder.Entity<Vehicle>()
            .Property(v => v.LengthCm)
            .HasPrecision(18, 2);
        builder.Entity<Vehicle>()
            .Property(v => v.WidthCm)
            .HasPrecision(18, 2);
        builder.Entity<Vehicle>()
            .Property(v => v.HeightCm)
            .HasPrecision(18, 2);

        builder.Entity<VehicleMaster>()
            .Property(v => v.MaxWeightKg)
            .HasPrecision(18, 2);
        builder.Entity<VehicleMaster>()
            .Property(v => v.LengthCm)
            .HasPrecision(18, 2);
        builder.Entity<VehicleMaster>()
            .Property(v => v.WidthCm)
            .HasPrecision(18, 2);
        builder.Entity<VehicleMaster>()
            .Property(v => v.HeightCm)
            .HasPrecision(18, 2);

        builder.Entity<Country>()
            .HasIndex(c => new { c.OrganizationId, c.Name })
            .IsUnique();

        builder.Entity<State>()
            .HasOne(s => s.Country)
            .WithMany()
            .HasForeignKey(s => s.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<City>()
            .HasOne(c => c.State)
            .WithMany()
            .HasForeignKey(c => c.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Region>()
            .HasIndex(r => new { r.OrganizationId, r.Name })
            .IsUnique();

        builder.Entity<Location>()
            .HasIndex(l => new { l.OrganizationId, l.Name })
            .IsUnique();

        builder.Entity<Location>()
            .HasOne(l => l.Region)
            .WithMany()
            .HasForeignKey(l => l.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Location>()
            .HasOne(l => l.State)
            .WithMany()
            .HasForeignKey(l => l.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Location>()
            .HasOne(l => l.City)
            .WithMany()
            .HasForeignKey(l => l.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Transporter>()
            .HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique();

        builder.Entity<VehicleType>()
            .HasIndex(t => new { t.OrganizationId, t.Name })
            .IsUnique();

        builder.Entity<VehicleCategory>()
            .HasIndex(c => new { c.OrganizationId, c.Name })
            .IsUnique();

        builder.Entity<Warehouse>()
            .HasIndex(w => new { w.OrganizationId, w.Name })
            .IsUnique();

        builder.Entity<Warehouse>()
            .Property(w => w.WarehouseType)
            .HasConversion<string>();

        builder.Entity<Warehouse>()
            .HasOne(w => w.Region)
            .WithMany()
            .HasForeignKey(w => w.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Warehouse>()
            .HasOne(w => w.State)
            .WithMany()
            .HasForeignKey(w => w.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Warehouse>()
            .HasOne(w => w.City)
            .WithMany()
            .HasForeignKey(w => w.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Warehouse>()
            .HasOne(w => w.Country)
            .WithMany()
            .HasForeignKey(w => w.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Warehouse)
            .WithMany()
            .HasForeignKey(u => u.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Region)
            .WithMany()
            .HasForeignKey(u => u.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Organization)
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Usernames are only unique within an organization (PlatformAdmin accounts, which have a
        // null OrganizationId, are excluded from this and kept distinct at the application layer -
        // see PlatformController). Replaces the ASP.NET Identity default's globally-unique
        // NormalizedUserName index.
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.NormalizedUserName)
            .IsUnique(false);
        builder.Entity<ApplicationUser>()
            .HasIndex(u => new { u.OrganizationId, u.NormalizedUserName })
            .IsUnique();

        builder.Entity<InwardTransaction>()
            .HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => t.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OutwardTransaction>()
            .HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => t.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AuditLog>()
            .Property(a => a.Action)
            .HasConversion<string>();

        builder.Entity<VehicleLogisticsRecord>()
            .HasOne(r => r.FromWarehouse)
            .WithMany()
            .HasForeignKey(r => r.FromWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VehicleLogisticsRecord>()
            .HasOne(r => r.ToWarehouse)
            .WithMany()
            .HasForeignKey(r => r.ToWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VehicleLogisticsRecord>()
            .Property(r => r.Status)
            .HasConversion<string>();

        builder.Entity<VehicleLogisticsRecord>()
            .HasIndex(r => r.FromWarehouseId);

        builder.Entity<VehicleLogisticsRecord>()
            .HasIndex(r => r.ToWarehouseId);

        builder.Entity<VehicleLogisticsRecord>()
            .HasIndex(r => r.VehicleNumber);

        builder.Entity<VehicleLogisticsRecord>()
            .HasIndex(r => r.Status);

        builder.Entity<VehicleLogisticsRecord>()
            .HasOne(r => r.ConsumedByInwardTransaction)
            .WithMany()
            .HasForeignKey(r => r.ConsumedByInwardTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<VehicleLogisticsRecord>()
            .HasOne(r => r.ConsumedByOutwardTransaction)
            .WithMany()
            .HasForeignKey(r => r.ConsumedByOutwardTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Filtered (not plain) unique index - SkuCode defaults to "" for un-configured Products,
        // and a plain unique index would only ever allow one such row per organization. MySQL has
        // no partial/filtered unique index syntax, so a generated column that's NULL whenever
        // SkuCode is empty stands in for the filter - unique indexes already allow unlimited NULLs
        // in both SQL Server and MySQL, which is exactly the "un-configured Products never collide"
        // behavior the SQL Server filter existed for.
        builder.Entity<Product>()
            .Property<string?>("SkuCodeForUniqueness")
            .HasComputedColumnSql("(CASE WHEN `SkuCode` <> '' THEN `SkuCode` ELSE NULL END)", stored: true);
        builder.Entity<Product>()
            .HasIndex("OrganizationId", "SkuCodeForUniqueness")
            .IsUnique();

        ConfigureTenantScoping(builder);
    }

    // Applies a global query filter, an FK relationship to Organization, and an indexed
    // OrganizationId column to every ITenantScoped/IOptionallyTenantScoped entity in one place,
    // so adding a new tenant-scoped entity later needs nothing beyond implementing the marker
    // interface - no per-controller or per-query filtering code required anywhere else.
    private void ConfigureTenantScoping(ModelBuilder builder)
    {
        var setTenantFilter = typeof(WarehouseGateDbContext)
            .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var setOptionalTenantFilter = typeof(WarehouseGateDbContext)
            .GetMethod(nameof(SetOptionalTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                setTenantFilter.MakeGenericMethod(clrType).Invoke(this, new object[] { builder });

                builder.Entity(clrType)
                    .HasOne(typeof(Organization))
                    .WithMany()
                    .HasForeignKey(nameof(ITenantScoped.OrganizationId))
                    .OnDelete(DeleteBehavior.Restrict);

                builder.Entity(clrType).HasIndex(nameof(ITenantScoped.OrganizationId));
            }
            else if (typeof(IOptionallyTenantScoped).IsAssignableFrom(clrType) && clrType != typeof(ApplicationUser))
            {
                // ApplicationUser configures its own Organization relationship/index above
                // (it already has an explicit navigation property, unlike the other entities here).
                setOptionalTenantFilter.MakeGenericMethod(clrType).Invoke(this, new object[] { builder });

                builder.Entity(clrType)
                    .HasOne(typeof(Organization))
                    .WithMany()
                    .HasForeignKey(nameof(IOptionallyTenantScoped.OrganizationId))
                    .OnDelete(DeleteBehavior.Restrict);

                builder.Entity(clrType).HasIndex(nameof(IOptionallyTenantScoped.OrganizationId));
            }
        }

        // ApplicationUser implements IOptionallyTenantScoped too but has its own explicit
        // Organization relationship/index configured above - just needs the query filter.
        setOptionalTenantFilter.MakeGenericMethod(typeof(ApplicationUser)).Invoke(this, new object[] { builder });
    }

    private void SetTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantScoped
    {
        builder.Entity<TEntity>().HasQueryFilter(e => !_tenant.HasContext || e.OrganizationId == _tenant.OrganizationId);
    }

    private void SetOptionalTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, IOptionallyTenantScoped
    {
        builder.Entity<TEntity>().HasQueryFilter(e => !_tenant.HasContext || e.OrganizationId == _tenant.OrganizationId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenantId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Auto-stamps OrganizationId onto newly-added tenant-scoped rows from the current request's
    // tenant context, so AdminController's Create endpoints (and everything else) don't need to
    // set it themselves. Entities created outside a request (SeedData, platform-level actions
    // that explicitly set OrganizationId themselves, e.g. a new org's first SuperAdmin) are left
    // alone - this only fills in a still-unset value.
    private void StampTenantId()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity is ITenantScoped tenantScoped && tenantScoped.OrganizationId == 0 && _tenant.OrganizationId.HasValue)
            {
                tenantScoped.OrganizationId = _tenant.OrganizationId.Value;
            }
            else if (entry.Entity is IOptionallyTenantScoped optionallyScoped && optionallyScoped.OrganizationId is null)
            {
                optionallyScoped.OrganizationId = _tenant.OrganizationId;
            }
        }
    }
}
