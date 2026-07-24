using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;

namespace WarehouseGate.Infrastructure;

public class WarehouseGateDbContext : IdentityDbContext<ApplicationUser>
{
    public WarehouseGateDbContext(DbContextOptions<WarehouseGateDbContext> options) : base(options)
    {
    }

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
    public DbSet<GoodsReceiptNote> GoodsReceiptNotes => Set<GoodsReceiptNote>();

    public DbSet<DispatchOrder> DispatchOrders => Set<DispatchOrder>();
    public DbSet<DispatchOrderLine> DispatchOrderLines => Set<DispatchOrderLine>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OutwardTransaction> OutwardTransactions => Set<OutwardTransaction>();
    public DbSet<OutwardPhotoEvidence> OutwardPhotoEvidences => Set<OutwardPhotoEvidence>();
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
            .HasIndex(v => v.Number)
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
            .HasIndex(v => new { v.VehicleTypeId, v.VehicleCategoryId })
            .IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasIndex(p => p.PONumber)
            .IsUnique();

        builder.Entity<InwardTransaction>()
            .HasIndex(t => t.InwardTxnNumber)
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

        builder.Entity<DispatchOrder>()
            .HasIndex(d => d.DispatchOrderNumber)
            .IsUnique();

        builder.Entity<OutwardTransaction>()
            .HasIndex(t => t.OutwardTxnNumber)
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
            .HasIndex(c => c.Name)
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
            .HasIndex(r => r.Name)
            .IsUnique();

        builder.Entity<Location>()
            .HasIndex(l => l.Name)
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
            .HasIndex(t => t.Name)
            .IsUnique();

        builder.Entity<VehicleType>()
            .HasIndex(t => t.Name)
            .IsUnique();

        builder.Entity<VehicleCategory>()
            .HasIndex(c => c.Name)
            .IsUnique();

        builder.Entity<Warehouse>()
            .HasIndex(w => w.Name)
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
        // and a plain unique index would only ever allow one such row in the whole table.
        builder.Entity<Product>()
            .HasIndex(p => p.SkuCode)
            .IsUnique()
            .HasFilter("[SkuCode] <> ''");
    }
}
