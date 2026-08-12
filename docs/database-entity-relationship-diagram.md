# WarehouseGate / LogiFlow database entity relationships

This document describes the EF Core model defined by `WarehouseGateDbContext` and the current
`WarehouseGateDbContextModelSnapshot`. It represents the persisted MySQL schema, not the mobile or
web DTO models.

## Legend

- `PK` — primary key
- `FK` — database-enforced foreign key
- `UK` — unique key/index (often tenant-scoped)
- `||` — exactly one
- `o|` — zero or one
- `o{` — zero or many
- Every entity implementing `ITenantScoped` has a required `OrganizationId` FK and a global tenant
  query filter. `ApplicationUser` and `AuditLog` have an optional tenant FK.

## High-level dependency map

```mermaid
flowchart LR
    ORG[Organization / tenant]
    GEO[Geography and warehouses]
    IAM[Users and Identity]
    MASTER[Vehicle, product and transporter masters]
    PLAN[Vehicle logistics / dispatch plan]
    IN[Inward receiving]
    OUT[Outward dispatch and 3D load planning]
    OPS[Audit and follow-up]

    ORG --> GEO
    ORG --> IAM
    ORG --> MASTER
    ORG --> PLAN
    ORG --> IN
    ORG --> OUT
    ORG --> OPS
    GEO --> IAM
    GEO --> PLAN
    GEO --> IN
    GEO --> OUT
    MASTER --> IN
    MASTER --> OUT
    PLAN --> IN
    PLAN --> OUT
    OUT -. creates destination receipt data .-> IN
```

## Tenant, geography, users and master data

```mermaid
erDiagram
    Organization {
        int Id PK
        string Code UK
        string Name
        bool IsActive
    }
    Country {
        int Id PK
        int OrganizationId FK
        string Name UK
    }
    State {
        int Id PK
        int OrganizationId FK
        int CountryId FK
        string Name
    }
    City {
        int Id PK
        int OrganizationId FK
        int StateId FK
        string Name
    }
    Region {
        int Id PK
        int OrganizationId FK
        string Name UK
    }
    Location {
        int Id PK
        int OrganizationId FK
        int RegionId FK
        int StateId FK
        int CityId FK
        string Name UK
    }
    Warehouse {
        int Id PK
        int OrganizationId FK
        int CountryId FK
        int StateId FK
        int CityId FK
        int RegionId FK
        string Name UK
    }
    DockBay {
        int Id PK
        int OrganizationId FK
        int WarehouseId FK
        string Name UK
    }
    ApplicationUser {
        string Id PK
        int OrganizationId FK "nullable"
        int WarehouseId FK "nullable"
        int RegionId FK "nullable"
        string UserName
    }
    Transporter {
        int Id PK
        int OrganizationId FK
        string Name UK
    }
    VehicleType {
        int Id PK
        int OrganizationId FK
        string Name UK
    }
    VehicleCategory {
        int Id PK
        int OrganizationId FK
        string Name UK
    }
    VehicleMaster {
        int Id PK
        int OrganizationId FK
        int VehicleTypeId FK
        int VehicleCategoryId FK
    }
    Vehicle {
        int Id PK
        int OrganizationId FK
        string Number UK
    }
    Product {
        int Id PK
        int OrganizationId FK
        string SkuCode UK "nullable computed key"
        string Name
    }

    Organization ||--o{ Country : owns
    Organization ||--o{ State : owns
    Organization ||--o{ City : owns
    Organization ||--o{ Region : owns
    Organization ||--o{ Location : owns
    Organization ||--o{ Warehouse : owns
    Organization ||--o{ DockBay : owns
    Organization o|--o{ ApplicationUser : owns
    Organization ||--o{ Transporter : owns
    Organization ||--o{ VehicleType : owns
    Organization ||--o{ VehicleCategory : owns
    Organization ||--o{ VehicleMaster : owns
    Organization ||--o{ Vehicle : owns
    Organization ||--o{ Product : owns

    Country ||--o{ State : contains
    State ||--o{ City : contains
    Region ||--o{ Location : groups
    State ||--o{ Location : locates
    City ||--o{ Location : locates
    Country ||--o{ Warehouse : locates
    State ||--o{ Warehouse : locates
    City ||--o{ Warehouse : locates
    Region ||--o{ Warehouse : groups
    Warehouse ||--o{ DockBay : contains
    Warehouse o|--o{ ApplicationUser : assigned_to
    Region o|--o{ ApplicationUser : assigned_to
    VehicleType ||--o{ VehicleMaster : classifies
    VehicleCategory ||--o{ VehicleMaster : classifies
```

Important unique constraints:

- `Organization.Code` is globally unique.
- Most master names are unique inside an organization: `(OrganizationId, Name)`.
- `Vehicle.Number` is unique inside an organization.
- A vehicle profile is unique by `(OrganizationId, VehicleTypeId, VehicleCategoryId)`.
- A dock name is unique by `(WarehouseId, Name)`.
- Non-empty product SKU codes are unique per organization through the computed
  `SkuCodeForUniqueness` column.

## Inward receiving

```mermaid
erDiagram
    Organization {
        int Id PK
    }
    Warehouse {
        int Id PK
    }
    Vehicle {
        int Id PK
    }
    Product {
        int Id PK
    }
    PurchaseOrder {
        int Id PK
        int OrganizationId FK
        string PONumber UK
    }
    PurchaseOrderLine {
        int Id PK
        int PurchaseOrderId FK
        int SourceDispatchOrderLineId "logical reference, nullable"
    }
    InwardTransaction {
        int Id PK
        int OrganizationId FK
        int VehicleId FK
        int WarehouseId FK "nullable"
        int PurchaseOrderId FK "nullable"
        string InwardTxnNumber UK
    }
    PhotoEvidence {
        int Id PK
        int InwardTransactionId FK
        int PurchaseOrderLineId FK "nullable"
    }
    InwardDocument {
        int Id PK
        int InwardTransactionId FK
    }
    InspectionLine {
        int Id PK
        int InwardTransactionId FK
        int PurchaseOrderLineId FK
    }
    UnplannedReceiptLine {
        int Id PK
        int InwardTransactionId FK
        int ProductId FK
    }
    GoodsReceiptNote {
        int Id PK
        int InwardTransactionId FK, UK
    }

    Organization ||--o{ PurchaseOrder : owns
    Organization ||--o{ InwardTransaction : owns
    PurchaseOrder ||--o{ PurchaseOrderLine : contains
    Vehicle ||--o{ InwardTransaction : arrives_as
    Warehouse o|--o{ InwardTransaction : receives_at
    PurchaseOrder o|--o{ InwardTransaction : linked_to
    InwardTransaction ||--o{ PhotoEvidence : has
    PurchaseOrderLine o|--o{ PhotoEvidence : documents_SKU
    InwardTransaction ||--o{ InwardDocument : has
    InwardTransaction ||--o{ InspectionLine : inspects
    PurchaseOrderLine ||--o{ InspectionLine : categorizes
    InwardTransaction ||--o{ UnplannedReceiptLine : receives_extra
    Product ||--o{ UnplannedReceiptLine : identifies
    InwardTransaction ||--o| GoodsReceiptNote : produces
```

Dependency notes:

- An inward transaction can be recorded before a purchase order is linked, so `PurchaseOrderId`
  is nullable.
- `GoodsReceiptNote.InwardTransactionId` is both an FK and unique, enforcing one GRN per inward job.
- Inspection rows depend on both the transaction and the expected PO line.
- `PurchaseOrderLine.SourceDispatchOrderLineId` is intentionally **not** an FK; it is a logical
  cross-flow trace to the source dispatch line.

## Outward dispatch and load planning

```mermaid
erDiagram
    Organization {
        int Id PK
    }
    Warehouse {
        int Id PK
    }
    Vehicle {
        int Id PK
    }
    Product {
        int Id PK
    }
    DispatchOrder {
        int Id PK
        int OrganizationId FK
        string DispatchOrderNumber UK
    }
    DispatchOrderLine {
        int Id PK
        int DispatchOrderId FK
        int ProductId FK "nullable"
    }
    OutwardGateArrival {
        int Id PK
        int OrganizationId FK
        int WarehouseId FK "nullable"
        int VehicleId FK
        int LinkedOutwardTransactionId FK "nullable"
    }
    OutwardGateArrivalPhoto {
        int Id PK
        int OutwardGateArrivalId FK
    }
    OutwardTransaction {
        int Id PK
        int OrganizationId FK
        int DispatchOrderId FK
        int WarehouseId FK "nullable"
        int VehicleId FK "nullable"
        string OutwardTxnNumber UK
    }
    OutwardLoadLine {
        int Id PK
        int OutwardTransactionId FK
        int DispatchOrderLineId FK
    }
    OutwardLoadPlanOption {
        int Id PK
        int OutwardTransactionId FK
    }
    OutwardLoadPlanGroup {
        int Id PK
        int OutwardLoadPlanOptionId FK
        int DispatchOrderLineId FK
    }
    OutwardPhotoEvidence {
        int Id PK
        int OutwardTransactionId FK
        int OutwardLoadPlanGroupId FK "nullable"
        int DispatchOrderLineId FK "nullable"
    }
    OutwardDispatchNote {
        int Id PK
        int OutwardTransactionId FK, UK
    }

    Organization ||--o{ DispatchOrder : owns
    Organization ||--o{ OutwardTransaction : owns
    Organization ||--o{ OutwardGateArrival : owns
    DispatchOrder ||--o{ DispatchOrderLine : contains
    Product o|--o{ DispatchOrderLine : describes
    Vehicle ||--o{ OutwardGateArrival : arrives_as
    Warehouse o|--o{ OutwardGateArrival : occurs_at
    OutwardTransaction o|--o{ OutwardGateArrival : linked_from
    OutwardGateArrival ||--o{ OutwardGateArrivalPhoto : has
    DispatchOrder ||--o{ OutwardTransaction : creates
    Warehouse o|--o{ OutwardTransaction : dispatches_from
    Vehicle o|--o{ OutwardTransaction : uses
    OutwardTransaction ||--o{ OutwardLoadLine : records
    DispatchOrderLine ||--o{ OutwardLoadLine : fulfills
    OutwardTransaction ||--o{ OutwardLoadPlanOption : proposes
    OutwardLoadPlanOption ||--o{ OutwardLoadPlanGroup : contains
    DispatchOrderLine ||--o{ OutwardLoadPlanGroup : places
    OutwardTransaction ||--o{ OutwardPhotoEvidence : has
    OutwardLoadPlanGroup o|--o{ OutwardPhotoEvidence : confirms_group
    DispatchOrderLine o|--o{ OutwardPhotoEvidence : confirms_SKU
    OutwardTransaction ||--o| OutwardDispatchNote : produces
```

Delete-behavior hotspots:

- Vehicle, warehouse, order and product master relationships are generally `RESTRICT` to preserve
  operational history.
- Deleting a load-plan option cascades to its groups.
- Deleting a load-plan group sets `OutwardPhotoEvidence.OutwardLoadPlanGroupId` to null, preserving
  the photo at transaction level.
- `OutwardDispatchNote.OutwardTransactionId` is unique, enforcing one dispatch note per job.

## Dispatch-plan bridge, audit and follow-up

```mermaid
erDiagram
    Organization {
        int Id PK
    }
    Warehouse {
        int Id PK
    }
    InwardTransaction {
        int Id PK
    }
    OutwardTransaction {
        int Id PK
    }
    VehicleLogisticsRecord {
        int Id PK
        int OrganizationId FK
        int FromWarehouseId FK
        int ToWarehouseId FK
        int ConsumedByInwardTransactionId FK "nullable"
        int ConsumedByOutwardTransactionId FK "nullable"
    }
    FollowUpTask {
        int Id PK
        int OrganizationId FK
        int WarehouseId FK "nullable"
        string EntityName "logical reference type"
        int EntityId "logical reference id"
    }
    AuditLog {
        int Id PK
        int OrganizationId FK "nullable"
        string EntityName "logical reference type"
        int EntityId "logical reference id"
        string ChangedByUserId "logical user id"
    }

    Organization ||--o{ VehicleLogisticsRecord : owns
    Warehouse ||--o{ VehicleLogisticsRecord : origin
    Warehouse ||--o{ VehicleLogisticsRecord : destination
    InwardTransaction o|--o{ VehicleLogisticsRecord : consumes
    OutwardTransaction o|--o{ VehicleLogisticsRecord : consumes
    Organization ||--o{ FollowUpTask : owns
    Warehouse o|--o{ FollowUpTask : scoped_to
    Organization o|--o{ AuditLog : owns
```

`VehicleLogisticsRecord` is the main cross-flow bridge. One planned shipment leg may first be
claimed by an outward job and later by an inward job, so both consumption FKs can be populated.

`FollowUpTask.EntityName/EntityId`, `AuditLog.EntityName/EntityId`, user-ID audit fields, and the
free-text `VehicleLogisticsRecord.InwardTransactionId` are polymorphic/logical references only;
the database does not enforce them as foreign keys.

## ASP.NET Identity relationships

```mermaid
erDiagram
    AspNetUsers {
        string Id PK
        int OrganizationId FK "nullable"
        int WarehouseId FK "nullable"
        int RegionId FK "nullable"
    }
    AspNetRoles {
        string Id PK
    }
    AspNetUserClaims {
        int Id PK
        string UserId FK
    }
    AspNetUserLogins {
        string LoginProvider PK
        string ProviderKey PK
        string UserId FK
    }
    AspNetUserRoles {
        string UserId PK, FK
        string RoleId PK, FK
    }
    AspNetUserTokens {
        string UserId PK, FK
        string LoginProvider PK
        string Name PK
    }
    AspNetRoleClaims {
        int Id PK
        string RoleId FK
    }

    AspNetUsers ||--o{ AspNetUserClaims : has
    AspNetUsers ||--o{ AspNetUserLogins : has
    AspNetUsers ||--o{ AspNetUserRoles : assigned
    AspNetRoles ||--o{ AspNetUserRoles : includes
    AspNetUsers ||--o{ AspNetUserTokens : has
    AspNetRoles ||--o{ AspNetRoleClaims : has
```

## Practical dependency order

For imports, test fixtures, or controlled deletes, the safe parent-to-child creation order is:

1. `Organization`
2. Geography (`Country` → `State` → `City`; `Region`)
3. `Warehouse` → `DockBay`, plus users and master data
4. `Product`, `Vehicle`, purchase/dispatch order headers
5. Purchase/dispatch order lines
6. Inward/outward transactions and outward gate arrivals
7. Evidence, documents, inspections, load lines, load-plan options/groups
8. GRN/dispatch notes, logistics consumption links, follow-up and audit rows

For deletion, reverse this order and honor the `RESTRICT` relationships. In practice, operational
transactions should be retained or soft-deactivated rather than physically deleted.

## Sources of truth

- `src/WarehouseGate.Infrastructure/WarehouseGateDbContext.cs`
- `src/WarehouseGate.Infrastructure/Migrations/WarehouseGateDbContextModelSnapshot.cs`
- `src/WarehouseGate.Domain/*.cs`
- `src/WarehouseGate.Infrastructure/ApplicationUser.cs`
