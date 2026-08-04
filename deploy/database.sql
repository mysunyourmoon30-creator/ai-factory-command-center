IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AiToolExecutionLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NULL,
        [ToolName] nvarchar(150) NOT NULL,
        [RequestId] nvarchar(150) NOT NULL,
        [RecordCount] int NOT NULL,
        [DurationMs] bigint NOT NULL,
        [Result] nvarchar(500) NOT NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        CONSTRAINT [PK_AiToolExecutionLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [Alerts] (
        [Id] bigint NOT NULL IDENTITY,
        [AlertType] nvarchar(30) NOT NULL,
        [Severity] nvarchar(30) NOT NULL,
        [EntityName] nvarchar(150) NOT NULL,
        [EntityId] bigint NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [ResolvedAt] datetime2(0) NULL,
        CONSTRAINT [PK_Alerts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] bigint NOT NULL IDENTITY,
        [IsActive] bit NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NULL,
        [Username] nvarchar(150) NOT NULL,
        [Action] nvarchar(150) NOT NULL,
        [EntityName] nvarchar(150) NOT NULL,
        [EntityId] bigint NULL,
        [Result] nvarchar(500) NOT NULL,
        [RequestId] nvarchar(150) NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [Formulations] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [BatchSize] decimal(18,3) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [UpdatedAt] datetime2(0) NOT NULL,
        CONSTRAINT [PK_Formulations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Formulations_BatchSize] CHECK ([BatchSize] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [Machines] (
        [Id] bigint NOT NULL IDENTITY,
        [MachineCode] nvarchar(30) NOT NULL,
        [MachineName] nvarchar(150) NOT NULL,
        [RunningStatus] nvarchar(30) NOT NULL,
        [Temperature] decimal(10,2) NOT NULL,
        [Speed] decimal(10,2) NOT NULL,
        [AlertStatus] nvarchar(30) NOT NULL,
        [LastUpdated] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Machines] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [RawMaterials] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Unit] nvarchar(30) NOT NULL,
        [CurrentStock] decimal(18,3) NOT NULL,
        [ReservedStock] decimal(18,3) NOT NULL,
        [LeadTimeDays] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [UpdatedAt] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_RawMaterials] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RawMaterials_CurrentStock] CHECK ([CurrentStock] >= 0),
        CONSTRAINT [CK_RawMaterials_LeadTimeDays] CHECK ([LeadTimeDays] >= 0),
        CONSTRAINT [CK_RawMaterials_ReservedStock] CHECK ([ReservedStock] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] bigint NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] bigint NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] bigint NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [CustomerOrders] (
        [Id] bigint NOT NULL IDENTITY,
        [OrderNumber] nvarchar(30) NOT NULL,
        [CustomerName] nvarchar(150) NOT NULL,
        [FormulationId] bigint NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [DeliveryDate] datetime2(0) NOT NULL,
        [Priority] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedByUserId] bigint NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [UpdatedAt] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CustomerOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CustomerOrders_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_CustomerOrders_Formulations_FormulationId] FOREIGN KEY ([FormulationId]) REFERENCES [Formulations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [FormulationMaterials] (
        [Id] bigint NOT NULL IDENTITY,
        [FormulationId] bigint NOT NULL,
        [RawMaterialId] bigint NOT NULL,
        [WeightPerBatch] decimal(18,3) NOT NULL,
        CONSTRAINT [PK_FormulationMaterials] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_FormulationMaterials_WeightPerBatch] CHECK ([WeightPerBatch] > 0),
        CONSTRAINT [FK_FormulationMaterials_Formulations_FormulationId] FOREIGN KEY ([FormulationId]) REFERENCES [Formulations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FormulationMaterials_RawMaterials_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [RawMaterials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductionPlans] (
        [Id] bigint NOT NULL IDENTITY,
        [PlanNumber] nvarchar(30) NOT NULL,
        [CustomerOrderId] bigint NOT NULL,
        [MachineId] bigint NOT NULL,
        [RequiredBatch] int NOT NULL,
        [PlannedCompletionDate] datetime2(0) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedByUserId] bigint NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ProductionPlans] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionPlans_RequiredBatch] CHECK ([RequiredBatch] > 0),
        CONSTRAINT [FK_ProductionPlans_CustomerOrders_CustomerOrderId] FOREIGN KEY ([CustomerOrderId]) REFERENCES [CustomerOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductionPlans_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [Machines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [MaterialRequirements] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionPlanId] bigint NOT NULL,
        [RawMaterialId] bigint NOT NULL,
        [RequiredQuantity] decimal(18,3) NOT NULL,
        [CalculatedAt] datetime2(0) NOT NULL,
        CONSTRAINT [PK_MaterialRequirements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MaterialRequirements_RequiredQuantity] CHECK ([RequiredQuantity] > 0),
        CONSTRAINT [FK_MaterialRequirements_ProductionPlans_ProductionPlanId] FOREIGN KEY ([ProductionPlanId]) REFERENCES [ProductionPlans] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MaterialRequirements_RawMaterials_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [RawMaterials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [PurchaseRequests] (
        [Id] bigint NOT NULL IDENTITY,
        [RequestNumber] nvarchar(30) NOT NULL,
        [SourceProductionPlanId] bigint NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [RequestedByUserId] bigint NOT NULL,
        [RequestedDate] datetime2(0) NOT NULL,
        [ApprovedByUserId] bigint NULL,
        [ApprovedDate] datetime2(0) NULL,
        [RejectionReason] nvarchar(500) NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PurchaseRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseRequests_ProductionPlans_SourceProductionPlanId] FOREIGN KEY ([SourceProductionPlanId]) REFERENCES [ProductionPlans] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [IncomingPurchaseOrders] (
        [Id] bigint NOT NULL IDENTITY,
        [PurchaseOrderNumber] nvarchar(30) NOT NULL,
        [PurchaseRequestId] bigint NOT NULL,
        [ExpectedDate] datetime2(0) NOT NULL,
        [ReceivedDate] datetime2(0) NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedAt] datetime2(0) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_IncomingPurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IncomingPurchaseOrders_PurchaseRequests_PurchaseRequestId] FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [PurchaseRequestItems] (
        [Id] bigint NOT NULL IDENTITY,
        [PurchaseRequestId] bigint NOT NULL,
        [RawMaterialId] bigint NOT NULL,
        [RequestedQuantity] decimal(18,3) NOT NULL,
        [ExpectedDate] datetime2(0) NOT NULL,
        CONSTRAINT [PK_PurchaseRequestItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PurchaseRequestItems_RequestedQuantity] CHECK ([RequestedQuantity] > 0),
        CONSTRAINT [FK_PurchaseRequestItems_PurchaseRequests_PurchaseRequestId] FOREIGN KEY ([PurchaseRequestId]) REFERENCES [PurchaseRequests] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseRequestItems_RawMaterials_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [RawMaterials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE TABLE [IncomingPurchaseOrderItems] (
        [Id] bigint NOT NULL IDENTITY,
        [IncomingPurchaseOrderId] bigint NOT NULL,
        [RawMaterialId] bigint NOT NULL,
        [OrderedQuantity] decimal(18,3) NOT NULL,
        [ReceivedQuantity] decimal(18,3) NOT NULL,
        CONSTRAINT [PK_IncomingPurchaseOrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_IncomingPOItems_OrderedQuantity] CHECK ([OrderedQuantity] > 0),
        CONSTRAINT [CK_IncomingPOItems_ReceivedQuantity] CHECK ([ReceivedQuantity] >= 0 AND [ReceivedQuantity] <= [OrderedQuantity]),
        CONSTRAINT [FK_IncomingPurchaseOrderItems_IncomingPurchaseOrders_IncomingPurchaseOrderId] FOREIGN KEY ([IncomingPurchaseOrderId]) REFERENCES [IncomingPurchaseOrders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_IncomingPurchaseOrderItems_RawMaterials_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [RawMaterials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AiToolExecutionLogs_ToolName_CreatedAt] ON [AiToolExecutionLogs] ([ToolName], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Alerts_AlertType_EntityName_EntityId] ON [Alerts] ([AlertType], [EntityName], [EntityId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Alerts_Severity_IsActive_CreatedAt] ON [Alerts] ([Severity], [IsActive], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Username_EntityName_CreatedAt] ON [AuditLogs] ([Username], [EntityName], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerOrders_DeliveryDate] ON [CustomerOrders] ([DeliveryDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerOrders_FormulationId] ON [CustomerOrders] ([FormulationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerOrders_OrderNumber] ON [CustomerOrders] ([OrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerOrders_Status] ON [CustomerOrders] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FormulationMaterials_FormulationId_RawMaterialId] ON [FormulationMaterials] ([FormulationId], [RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FormulationMaterials_RawMaterialId] ON [FormulationMaterials] ([RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Formulations_Code] ON [Formulations] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IncomingPurchaseOrderItems_IncomingPurchaseOrderId_RawMaterialId] ON [IncomingPurchaseOrderItems] ([IncomingPurchaseOrderId], [RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IncomingPurchaseOrderItems_RawMaterialId] ON [IncomingPurchaseOrderItems] ([RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IncomingPurchaseOrders_ExpectedDate] ON [IncomingPurchaseOrders] ([ExpectedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IncomingPurchaseOrders_PurchaseOrderNumber] ON [IncomingPurchaseOrders] ([PurchaseOrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IncomingPurchaseOrders_PurchaseRequestId] ON [IncomingPurchaseOrders] ([PurchaseRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IncomingPurchaseOrders_Status] ON [IncomingPurchaseOrders] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Machines_MachineCode] ON [Machines] ([MachineCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MaterialRequirements_ProductionPlanId_RawMaterialId] ON [MaterialRequirements] ([ProductionPlanId], [RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MaterialRequirements_RawMaterialId] ON [MaterialRequirements] ([RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductionPlans_CustomerOrderId] ON [ProductionPlans] ([CustomerOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductionPlans_MachineId] ON [ProductionPlans] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductionPlans_PlannedCompletionDate] ON [ProductionPlans] ([PlannedCompletionDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductionPlans_PlanNumber] ON [ProductionPlans] ([PlanNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductionPlans_Status] ON [ProductionPlans] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseRequestItems_PurchaseRequestId_RawMaterialId] ON [PurchaseRequestItems] ([PurchaseRequestId], [RawMaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequestItems_RawMaterialId_PurchaseRequestId] ON [PurchaseRequestItems] ([RawMaterialId], [PurchaseRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseRequests_RequestNumber] ON [PurchaseRequests] ([RequestNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequests_SourceProductionPlanId_Status] ON [PurchaseRequests] ([SourceProductionPlanId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RawMaterials_Code] ON [RawMaterials] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804125506_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804125506_InitialCreate', N'10.0.10');
END;

COMMIT;
GO

