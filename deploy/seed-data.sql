-- Fallback SQL data script (locked deploy/ layout: run after database.sql).
-- Generated from a freshly-seeded canonical database via the Day 13 verification harness -
-- NOT hand-written, so Identity password hashes match Demo@12345 exactly. Regenerate if the
-- canonical seed data changes; do not hand-edit.
SET NOCOUNT ON;

SET IDENTITY_INSERT [AspNetRoles] ON;
INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES
(1, N'Admin', N'ADMIN', N'b17e4358-65d9-4b48-acba-af367f15bc81'),
(2, N'Manager', N'MANAGER', N'd3d90420-6941-43c9-8ddf-d2b7a7021f73'),
(3, N'Planner', N'PLANNER', N'2be71b3a-a8c5-4666-9aab-a38c5f7f207b'),
(4, N'Viewer', N'VIEWER', N'675cf494-e72b-417a-bea0-dc9725e89f8c');
SET IDENTITY_INSERT [AspNetRoles] OFF;

SET IDENTITY_INSERT [AspNetUsers] ON;
INSERT INTO [AspNetUsers] ([Id], [IsActive], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed], [PasswordHash], [SecurityStamp], [ConcurrencyStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnd], [LockoutEnabled], [AccessFailedCount]) VALUES
(1, 1, N'admin.demo', N'ADMIN.DEMO', NULL, NULL, 0, N'AQAAAAIAAYagAAAAEE6XC1x+mlXEnLBj0G9MUroYfboLUDyySbcBAfQ0qXeM1rLQGU9QpTmHzEEyJUFK0g==', N'XHHTQNZQVLKNYD2FS6WLUGJFT74F2HMP', N'bbb432ec-2cc7-46e4-a5cb-5e318b18c586', NULL, 0, 0, NULL, 1, 0),
(2, 1, N'manager.demo', N'MANAGER.DEMO', NULL, NULL, 0, N'AQAAAAIAAYagAAAAEKQvvZ65kHRO4RLEc4SucQwAOlbb+TdG7RofiBwV/+GU4oub9itwzddx32uH1UogtQ==', N'C6BCDX4TEMZVADRID7CLBSHQ6KG7QNED', N'7371528d-5120-4b8a-a4dd-b0e3822938ef', NULL, 0, 0, NULL, 1, 0),
(3, 1, N'planner.demo', N'PLANNER.DEMO', NULL, NULL, 0, N'AQAAAAIAAYagAAAAEBZsDlBxplKg/7fwfzTLstxQksMI6hkjR9nx9kiOkwqzM99O0vQWSzKHqX/c115d3w==', N'UYSJCCKVN6NRIH33OHOCTGSI2V4VPMGY', N'fa4eafec-916d-40fc-80df-17241dde940f', NULL, 0, 0, NULL, 1, 0),
(4, 1, N'viewer.demo', N'VIEWER.DEMO', NULL, NULL, 0, N'AQAAAAIAAYagAAAAEFUbGBZnoDFfE9zlVcMiAZ2/4TaFrL6sOoJkm6RBaMP7GjPmbeGcV6VqNkRB/mkbWQ==', N'5BJRUDE5LZAWLNABO2TJUQQ6NDXDASN5', N'aa1074c8-343d-4c2d-9e2c-586a4633a39b', NULL, 0, 0, NULL, 1, 0);
SET IDENTITY_INSERT [AspNetUsers] OFF;

INSERT INTO [AspNetUserRoles] ([UserId], [RoleId]) VALUES
(1, 1),
(2, 2),
(3, 3),
(4, 4);

SET IDENTITY_INSERT [RawMaterials] ON;
INSERT INTO [RawMaterials] ([Id], [Code], [Name], [Unit], [CurrentStock], [ReservedStock], [LeadTimeDays], [IsActive], [CreatedAt], [UpdatedAt]) VALUES
(1, N'RM-001', N'Polymer Base A', N'kg', 4200.000, 450.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(2, N'RM-002', N'Additive B', N'kg', 5000.000, 300.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(3, N'RM-003', N'Pigment C', N'kg', 3500.000, 100.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(4, N'RM-004', N'Filler D', N'kg', 10000.000, 500.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(5, N'RM-005', N'Binder E', N'kg', 8000.000, 400.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(6, N'RM-006', N'Catalyst F', N'kg', 5000.000, 200.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(7, N'RM-007', N'Stabilizer G', N'kg', 6000.000, 300.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(8, N'RM-008', N'Solvent H', N'kg', 9000.000, 500.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(9, N'RM-009', N'Resin I', N'kg', 7000.000, 300.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(10, N'RM-010', N'Modifier J', N'kg', 4000.000, 100.000, 7, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000');
SET IDENTITY_INSERT [RawMaterials] OFF;

SET IDENTITY_INSERT [Formulations] ON;
INSERT INTO [Formulations] ([Id], [Code], [Name], [BatchSize], [IsActive], [CreatedAt], [UpdatedAt]) VALUES
(1, N'FM-DEMO-001', N'Demo Formulation 001', 500.000, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(2, N'FM-002', N'Formulation 002', 1000.000, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(3, N'FM-003', N'Formulation 003', 750.000, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(4, N'FM-004', N'Formulation 004', 600.000, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000'),
(5, N'FM-005', N'Formulation 005', 400.000, 1, '2026-08-06T14:20:39.0000000', '2026-08-06T14:20:39.0000000');
SET IDENTITY_INSERT [Formulations] OFF;

SET IDENTITY_INSERT [Machines] ON;
INSERT INTO [Machines] ([Id], [MachineCode], [MachineName], [RunningStatus], [Temperature], [Speed], [AlertStatus], [LastUpdated]) VALUES
(1, N'Machine-01', N'Machine 01', N'Running', 72.00, 80.00, N'Normal', '2026-08-06T00:00:00.0000000'),
(2, N'Machine-02', N'Machine 02', N'Running', 95.00, 60.00, N'Critical', '2026-08-06T00:00:00.0000000'),
(3, N'Machine-03', N'Machine 03', N'Stopped', 35.00, 0.00, N'Warning', '2026-08-06T00:00:00.0000000');
SET IDENTITY_INSERT [Machines] OFF;

SET IDENTITY_INSERT [FormulationMaterials] ON;
INSERT INTO [FormulationMaterials] ([Id], [FormulationId], [RawMaterialId], [WeightPerBatch]) VALUES
(1, 1, 1, 250.000),
(2, 1, 2, 150.000),
(3, 1, 3, 100.000),
(4, 2, 4, 400.000),
(5, 2, 5, 350.000),
(6, 2, 6, 250.000),
(7, 3, 2, 250.000),
(8, 3, 7, 250.000),
(9, 3, 8, 250.000),
(10, 4, 3, 200.000),
(11, 4, 9, 250.000),
(12, 4, 10, 150.000),
(13, 5, 5, 150.000),
(14, 5, 8, 100.000),
(15, 5, 9, 100.000),
(16, 5, 10, 50.000);
SET IDENTITY_INSERT [FormulationMaterials] OFF;

SET IDENTITY_INSERT [CustomerOrders] ON;
INSERT INTO [CustomerOrders] ([Id], [OrderNumber], [CustomerName], [FormulationId], [Quantity], [DeliveryDate], [Priority], [Status], [CreatedByUserId], [CreatedAt], [UpdatedAt]) VALUES
(1, N'SO-DEMO-001', N'Demo Customer DEMO-001', 1, 10000.000, '2026-08-09T00:00:00.0000000', N'Urgent', N'Planned', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(2, N'SO-0002', N'Demo Customer 0002', 2, 5000.000, '2026-08-16T00:00:00.0000000', N'High', N'Planned', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(3, N'SO-0003', N'Demo Customer 0003', 3, 2250.000, '2026-08-13T00:00:00.0000000', N'Normal', N'InProduction', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(4, N'SO-0004', N'Demo Customer 0004', 4, 1200.000, '2026-08-04T00:00:00.0000000', N'Normal', N'Completed', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(5, N'SO-0005', N'Demo Customer 0005', 5, 800.000, '2026-08-20T00:00:00.0000000', N'Normal', N'Draft', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(6, N'SO-0006', N'Demo Customer 0006', 2, 2000.000, '2026-08-18T00:00:00.0000000', N'Normal', N'Planned', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(7, N'SO-0007', N'Demo Customer 0007', 3, 1500.000, '2026-08-05T00:00:00.0000000', N'Normal', N'Completed', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(8, N'SO-0008', N'Demo Customer 0008', 4, 1800.000, '2026-08-15T00:00:00.0000000', N'High', N'Planned', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(9, N'SO-0009', N'Demo Customer 0009', 5, 1600.000, '2026-08-17T00:00:00.0000000', N'Normal', N'Planned', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000'),
(10, N'SO-0010', N'Demo Customer 0010', 3, 750.000, '2026-08-21T00:00:00.0000000', N'Normal', N'Draft', 3, '2026-08-06T00:00:00.0000000', '2026-08-06T00:00:00.0000000');
SET IDENTITY_INSERT [CustomerOrders] OFF;

SET IDENTITY_INSERT [ProductionPlans] ON;
INSERT INTO [ProductionPlans] ([Id], [PlanNumber], [CustomerOrderId], [MachineId], [RequiredBatch], [PlannedCompletionDate], [Status], [CreatedByUserId], [CreatedAt]) VALUES
(1, N'PP-DEMO-001', 1, 2, 20, '2026-08-11T00:00:00.0000000', N'Planned', 3, '2026-08-06T00:00:00.0000000'),
(2, N'PP-0002', 2, 1, 5, '2026-08-10T00:00:00.0000000', N'Planned', 3, '2026-08-06T00:00:00.0000000'),
(3, N'PP-0003', 3, 1, 3, '2026-08-08T00:00:00.0000000', N'InProduction', 3, '2026-08-06T00:00:00.0000000'),
(4, N'PP-0004', 4, 3, 2, '2026-08-01T00:00:00.0000000', N'Completed', 3, '2026-08-06T00:00:00.0000000'),
(5, N'PP-0006', 6, 1, 2, '2026-08-11T00:00:00.0000000', N'Planned', 3, '2026-08-06T00:00:00.0000000'),
(6, N'PP-0007', 7, 1, 2, '2026-08-03T00:00:00.0000000', N'Completed', 3, '2026-08-06T00:00:00.0000000'),
(7, N'PP-0008', 8, 3, 3, '2026-08-08T00:00:00.0000000', N'Planned', 3, '2026-08-06T00:00:00.0000000'),
(8, N'PP-0009', 9, 1, 4, '2026-08-09T00:00:00.0000000', N'Planned', 3, '2026-08-06T00:00:00.0000000');
SET IDENTITY_INSERT [ProductionPlans] OFF;

SET IDENTITY_INSERT [MaterialRequirements] ON;
INSERT INTO [MaterialRequirements] ([Id], [ProductionPlanId], [RawMaterialId], [RequiredQuantity], [CalculatedAt]) VALUES
(1, 1, 1, 5000.000, '2026-08-06T00:00:00.0000000'),
(2, 1, 2, 3000.000, '2026-08-06T00:00:00.0000000'),
(3, 1, 3, 2000.000, '2026-08-06T00:00:00.0000000'),
(4, 2, 4, 2000.000, '2026-08-06T00:00:00.0000000'),
(5, 2, 5, 1750.000, '2026-08-06T00:00:00.0000000'),
(6, 2, 6, 1250.000, '2026-08-06T00:00:00.0000000'),
(7, 3, 2, 750.000, '2026-08-06T00:00:00.0000000'),
(8, 3, 7, 750.000, '2026-08-06T00:00:00.0000000'),
(9, 3, 8, 750.000, '2026-08-06T00:00:00.0000000'),
(10, 4, 3, 400.000, '2026-08-06T00:00:00.0000000'),
(11, 4, 9, 500.000, '2026-08-06T00:00:00.0000000'),
(12, 4, 10, 300.000, '2026-08-06T00:00:00.0000000'),
(13, 5, 4, 800.000, '2026-08-06T00:00:00.0000000'),
(14, 5, 5, 700.000, '2026-08-06T00:00:00.0000000'),
(15, 5, 6, 500.000, '2026-08-06T00:00:00.0000000'),
(16, 6, 2, 500.000, '2026-08-06T00:00:00.0000000'),
(17, 6, 7, 500.000, '2026-08-06T00:00:00.0000000'),
(18, 6, 8, 500.000, '2026-08-06T00:00:00.0000000'),
(19, 7, 3, 600.000, '2026-08-06T00:00:00.0000000'),
(20, 7, 9, 750.000, '2026-08-06T00:00:00.0000000'),
(21, 7, 10, 450.000, '2026-08-06T00:00:00.0000000'),
(22, 8, 5, 600.000, '2026-08-06T00:00:00.0000000'),
(23, 8, 8, 400.000, '2026-08-06T00:00:00.0000000'),
(24, 8, 9, 400.000, '2026-08-06T00:00:00.0000000'),
(25, 8, 10, 200.000, '2026-08-06T00:00:00.0000000');
SET IDENTITY_INSERT [MaterialRequirements] OFF;

-- PurchaseRequests: no canonical rows to seed.

-- PurchaseRequestItems: no canonical rows to seed.

-- IncomingPurchaseOrders: no canonical rows to seed.

-- IncomingPurchaseOrderItems: no canonical rows to seed.

-- Alerts: no canonical rows to seed.

-- AuditLogs: no canonical rows to seed.

-- AiToolExecutionLogs: no canonical rows to seed.

