-- ============================================================
-- Quality Gates - Tabla TRA_PROJECT_GATES (eslabón faltante)
-- Base de datos: SRM
-- Requisito: ejecutar DESPUÉS de Quality Gates - New TRA Tables.sql
--
-- PROBLEMA QUE RESUELVE:
--   TRA_DELIVERABLES referenciaba MAS_DELIVERABLES directamente
--   sin pasar por una instancia transaccional de la gate/acción.
--   Esta tabla crea ese eslabón intermedio:
--
--   TRA_PROJECTS → TRA_PROJECT_GATES → TRA_DELIVERABLES
--                        ↑
--              instancia la gate por proyecto con estado,
--              responsable asignado y fechas planeadas/reales
-- ============================================================

USE [SRM]
GO

-- ============================================================
-- SECCIÓN 1: CATÁLOGO DE ESTADOS DE GATE
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MAS_GATE_STATUS' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[MAS_GATE_STATUS] (
        [GATE_STATUS_ID]    [nchar](10)     NOT NULL,
        [GATE_STATUS_DESC]  [nvarchar](50)  NOT NULL,
        [STATUS_ORDER]      [int]           NOT NULL,
        [IS_FINAL_STATE]    [bit]           NOT NULL
            CONSTRAINT [DF_MAS_GATE_STATUS_IS_FINAL] DEFAULT (0),
        CONSTRAINT [PK_MAS_GATE_STATUS] PRIMARY KEY CLUSTERED ([GATE_STATUS_ID] ASC)
    )
    PRINT 'Tabla MAS_GATE_STATUS creada.'
END
ELSE
    PRINT 'Tabla MAS_GATE_STATUS ya existe, se omite.'
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[MAS_GATE_STATUS])
BEGIN
    INSERT INTO [dbo].[MAS_GATE_STATUS]
        ([GATE_STATUS_ID], [GATE_STATUS_DESC], [STATUS_ORDER], [IS_FINAL_STATE])
    VALUES
        ('NOTSTARTED', 'No Iniciada',       1, 0),
        ('INPROGRES',  'En Progreso',       2, 0),
        ('COMPLETED',  'Completada',        3, 1),
        ('BLOCKED',    'Bloqueada',         4, 0),
        ('WAIVED',     'Dispensada',        5, 1)
    PRINT 'Datos iniciales MAS_GATE_STATUS insertados.'
END
GO

-- ============================================================
-- SECCIÓN 2: TRA_PROJECT_GATES
-- Una fila por cada combinación proyecto × gate master.
-- Es el eslabón entre TRA_PROJECTS y TRA_DELIVERABLES.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TRA_PROJECT_GATES' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[TRA_PROJECT_GATES] (
        -- Surrogate PK — permite que TRA_DELIVERABLES referencie con 1 sola columna
        [TRA_GATE_ID]           [int]           IDENTITY(1,1) NOT NULL,

        -- Proyecto (línea de oportunidad de Salesforce)
        [OPP_LINE_ID]           [nvarchar](50)  NOT NULL,

        -- Clave compuesta a la gate master (MAS_SUB_MODULES)
        [MODULE_ID]             [nchar](10)     NOT NULL,
        [STATUS_ID]             [nchar](10)     NOT NULL,
        [SGATE_ID]              [nvarchar](10)  NOT NULL,

        -- Estado de la gate para este proyecto
        [GATE_STATUS_ID]        [nchar](10)     NOT NULL
            CONSTRAINT [DF_TRA_PROJECT_GATES_STATUS] DEFAULT ('NOTSTARTED'),

        -- Responsable asignado para esta gate en este proyecto.
        -- Se resuelve según la fase:
        --   Feasibility      → Business Unit Director
        --   Design           → R&D Director
        --   Industrialización → Plant Manager del centro de fabricación
        --   SOP              → Project Manager
        [RESPONSIBLE_USER_ID]   [nvarchar](50)  NULL,

        -- Fechas planeadas (calculadas al crear el proyecto:
        --   fecha inicio proyecto + PROCESS_DAYS acumulados de MAS_SUB_MODULES)
        [PLANNED_START_DATE]    [date]          NULL,
        [PLANNED_END_DATE]      [date]          NULL,

        -- Fechas reales registradas durante la ejecución
        [ACTUAL_START_DATE]     [date]          NULL,
        [ACTUAL_END_DATE]       [date]          NULL,

        -- Auditoría
        [CREATED_BY]            [nvarchar](50)  NOT NULL,
        [CREATED_DATE]          [datetime2](7)  NOT NULL
            CONSTRAINT [DF_TRA_PROJECT_GATES_CREATED_DATE] DEFAULT (GETDATE()),
        [MODIFIED_BY]           [nvarchar](50)  NULL,
        [MODIFIED_DATE]         [datetime2](7)  NULL,

        CONSTRAINT [PK_TRA_PROJECT_GATES] PRIMARY KEY CLUSTERED ([TRA_GATE_ID] ASC),

        -- Una sola instancia por proyecto × gate master
        CONSTRAINT [UQ_TRA_PROJECT_GATES_PROJECT_GATE]
            UNIQUE ([OPP_LINE_ID], [MODULE_ID], [STATUS_ID], [SGATE_ID])
    )
    PRINT 'Tabla TRA_PROJECT_GATES creada.'
END
ELSE
    PRINT 'Tabla TRA_PROJECT_GATES ya existe, se omite.'
GO

-- ============================================================
-- SECCIÓN 3: AÑADIR TRA_GATE_ID A TRA_DELIVERABLES
-- Vincula cada entregable de proyecto con su gate transaccional.
-- NULL inicialmente para no romper filas existentes.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE name = 'TRA_GATE_ID'
      AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLES')
)
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLES]
        ADD [TRA_GATE_ID] [int] NULL
    PRINT 'Columna TRA_GATE_ID añadida a TRA_DELIVERABLES.'
END
ELSE
    PRINT 'Columna TRA_GATE_ID ya existe en TRA_DELIVERABLES, se omite.'
GO

-- ============================================================
-- SECCIÓN 4: FOREIGN KEYS
-- ============================================================

-- TRA_PROJECT_GATES → MAS_SUB_MODULES (gate master)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_PROJECT_GATES_MAS_SUB_MODULES')
BEGIN
    ALTER TABLE [dbo].[TRA_PROJECT_GATES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_PROJECT_GATES_MAS_SUB_MODULES]
        FOREIGN KEY ([MODULE_ID], [STATUS_ID], [SGATE_ID])
        REFERENCES [dbo].[MAS_SUB_MODULES] ([MODULE_ID], [STATUS_ID], [SGATE_ID])
    PRINT 'FK FK_TRA_PROJECT_GATES_MAS_SUB_MODULES creada.'
END
GO

-- TRA_PROJECT_GATES → MAS_GATE_STATUS
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_PROJECT_GATES_STATUS')
BEGIN
    ALTER TABLE [dbo].[TRA_PROJECT_GATES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_PROJECT_GATES_STATUS]
        FOREIGN KEY ([GATE_STATUS_ID])
        REFERENCES [dbo].[MAS_GATE_STATUS] ([GATE_STATUS_ID])
    PRINT 'FK FK_TRA_PROJECT_GATES_STATUS creada.'
END
GO

-- TRA_PROJECT_GATES → MAS_USERS_CADENA (responsable)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_PROJECT_GATES_RESPONSIBLE')
BEGIN
    ALTER TABLE [dbo].[TRA_PROJECT_GATES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_PROJECT_GATES_RESPONSIBLE]
        FOREIGN KEY ([RESPONSIBLE_USER_ID])
        REFERENCES [dbo].[MAS_USERS_CADENA] ([id])
    PRINT 'FK FK_TRA_PROJECT_GATES_RESPONSIBLE creada.'
END
GO

-- TRA_DELIVERABLES → TRA_PROJECT_GATES
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_DELIVERABLES_PROJECT_GATE')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_DELIVERABLES_PROJECT_GATE]
        FOREIGN KEY ([TRA_GATE_ID])
        REFERENCES [dbo].[TRA_PROJECT_GATES] ([TRA_GATE_ID])
    PRINT 'FK FK_TRA_DELIVERABLES_PROJECT_GATE creada.'
END
GO

-- ============================================================
-- SECCIÓN 5: ÍNDICES
-- ============================================================

-- Todas las gates de un proyecto (vista de progreso por proyecto)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_PROJECT_GATES_OPP_LINE' AND object_id = OBJECT_ID('dbo.TRA_PROJECT_GATES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_PROJECT_GATES_OPP_LINE]
        ON [dbo].[TRA_PROJECT_GATES] ([OPP_LINE_ID])
        INCLUDE ([MODULE_ID], [STATUS_ID], [SGATE_ID], [GATE_STATUS_ID], [PLANNED_END_DATE], [ACTUAL_END_DATE])
GO

-- Gates con retraso (fecha fin planeada superada y no completadas)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_PROJECT_GATES_OVERDUE' AND object_id = OBJECT_ID('dbo.TRA_PROJECT_GATES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_PROJECT_GATES_OVERDUE]
        ON [dbo].[TRA_PROJECT_GATES] ([PLANNED_END_DATE], [GATE_STATUS_ID])
        INCLUDE ([OPP_LINE_ID], [MODULE_ID], [STATUS_ID], [SGATE_ID], [RESPONSIBLE_USER_ID])
GO

-- Búsqueda por responsable (panel de trabajo del usuario)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_PROJECT_GATES_RESPONSIBLE' AND object_id = OBJECT_ID('dbo.TRA_PROJECT_GATES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_PROJECT_GATES_RESPONSIBLE]
        ON [dbo].[TRA_PROJECT_GATES] ([RESPONSIBLE_USER_ID], [GATE_STATUS_ID])
        INCLUDE ([OPP_LINE_ID], [TRA_GATE_ID], [PLANNED_END_DATE])
GO

PRINT '====================================='
PRINT 'Script TRA_PROJECT_GATES ejecutado correctamente.'
PRINT '====================================='
GO
