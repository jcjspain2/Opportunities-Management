-- ============================================================
-- Quality Gates - Tablas Transaccionales Nuevas
-- Base de datos: SRM
-- Script independiente: requiere que el schema base (MAS_ y TRA_)
-- ya exista. Ejecutar en orden completo.
-- ============================================================

USE [SRM]
GO

-- ============================================================
-- SECCIÓN 1: CATÁLOGOS NUEVOS (MAS_)
-- ============================================================

-- 1.1 Catálogo de job titles (normaliza RACI en MAS_DELIVERABLES)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MAS_JOB_TITLES' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[MAS_JOB_TITLES] (
        [JOB_TITLE_ID]          [nchar](10)     NOT NULL,
        [JOB_TITLE_DESCRIPTION] [nvarchar](100) NOT NULL,
        CONSTRAINT [PK_MAS_JOB_TITLES] PRIMARY KEY CLUSTERED ([JOB_TITLE_ID] ASC)
    )
    PRINT 'Tabla MAS_JOB_TITLES creada.'
END
ELSE
    PRINT 'Tabla MAS_JOB_TITLES ya existe, se omite.'
GO

-- 1.2 Catálogo de estados para los entregables transaccionales
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MAS_DELIVERABLE_STATUS' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[MAS_DELIVERABLE_STATUS] (
        [DELIVERABLE_STATUS_ID]   [nchar](10)     NOT NULL,
        [DELIVERABLE_STATUS_DESC] [nvarchar](50)  NOT NULL,
        [STATUS_ORDER]            [int]           NOT NULL,
        [IS_FINAL_STATE]          [bit]           NOT NULL
            CONSTRAINT [DF_MAS_DELIVERABLE_STATUS_IS_FINAL] DEFAULT (0),
        CONSTRAINT [PK_MAS_DELIVERABLE_STATUS] PRIMARY KEY CLUSTERED ([DELIVERABLE_STATUS_ID] ASC)
    )
    PRINT 'Tabla MAS_DELIVERABLE_STATUS creada.'
END
ELSE
    PRINT 'Tabla MAS_DELIVERABLE_STATUS ya existe, se omite.'
GO

-- ============================================================
-- SECCIÓN 2: TABLAS TRANSACCIONALES (TRA_)
-- ============================================================

-- 2.1 TRA_DELIVERABLES
-- Una fila por cada combinación proyecto × entregable master.
-- Registra el estado real, fechas planificadas vs reales,
-- el responsable asignado y (si el tipo es texto) el contenido.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TRA_DELIVERABLES' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[TRA_DELIVERABLES] (
        [TRA_DELIVERABLE_ID]    [int]           IDENTITY(1,1) NOT NULL,

        -- Proyecto (línea de oportunidad de Salesforce)
        [OPP_LINE_ID]           [nvarchar](50)  NOT NULL,

        -- Clave compuesta al entregable master (MAS_DELIVERABLES)
        [MODULE_ID]             [nchar](10)     NOT NULL,
        [STATUS_ID]             [nchar](10)     NOT NULL,
        [SGATE_ID]              [nvarchar](10)  NOT NULL,
        [SEQUENCE]              [int]           NOT NULL,

        -- Estado
        [DELIVERABLE_STATUS_ID] [nchar](10)     NOT NULL
            CONSTRAINT [DF_TRA_DELIVERABLES_STATUS] DEFAULT ('PENDING'),

        -- Fechas planeadas (calculadas al crear el proyecto según lead times)
        [PLANNED_START_DATE]    [date]          NULL,
        [PLANNED_END_DATE]      [date]          NULL,

        -- Fechas reales
        [ACTUAL_START_DATE]     [date]          NULL,
        [ACTUAL_END_DATE]       [date]          NULL,

        -- Personas asignadas (resueltas desde MAS_USERS_CADENA)
        [RESPONSIBLE_USER_ID]   [nvarchar](50)  NULL,
        [ACCOUNTABLE_USER_ID]   [nvarchar](50)  NULL,

        -- Contenido de texto (solo si DELIVERABLE_TYPE.IS_TEXT = 1)
        [TEXT_CONTENT]          [nvarchar](max) NULL,

        -- Auditoría
        [CREATED_BY]            [nvarchar](50)  NOT NULL,
        [CREATED_DATE]          [datetime2](7)  NOT NULL
            CONSTRAINT [DF_TRA_DELIVERABLES_CREATED_DATE] DEFAULT (GETDATE()),
        [MODIFIED_BY]           [nvarchar](50)  NULL,
        [MODIFIED_DATE]         [datetime2](7)  NULL,

        CONSTRAINT [PK_TRA_DELIVERABLES] PRIMARY KEY CLUSTERED ([TRA_DELIVERABLE_ID] ASC),

        -- Evita duplicados: solo 1 instancia por proyecto × entregable master
        CONSTRAINT [UQ_TRA_DELIVERABLES_PROJECT_DELIVERABLE]
            UNIQUE ([OPP_LINE_ID], [MODULE_ID], [STATUS_ID], [SGATE_ID], [SEQUENCE])
    )
    PRINT 'Tabla TRA_DELIVERABLES creada.'
END
ELSE
    PRINT 'Tabla TRA_DELIVERABLES ya existe, se omite.'
GO

-- 2.2 TRA_DELIVERABLE_FILES
-- Almacena los ficheros subidos para un entregable de proyecto.
-- Soporta múltiples versiones; IS_CURRENT_VERSION marca la activa.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TRA_DELIVERABLE_FILES' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[TRA_DELIVERABLE_FILES] (
        [FILE_ID]               [int]           IDENTITY(1,1) NOT NULL,
        [TRA_DELIVERABLE_ID]    [int]           NOT NULL,
        [FILE_NAME]             [nvarchar](255) NOT NULL,
        -- Ruta en SharePoint, blob storage o servidor de ficheros
        [FILE_PATH]             [nvarchar](500) NOT NULL,
        [FILE_VERSION]          [int]           NOT NULL
            CONSTRAINT [DF_TRA_FILES_VERSION] DEFAULT (1),
        [FILE_SIZE_KB]          [int]           NULL,
        [MIME_TYPE]             [nvarchar](100) NULL,
        -- Solo la versión más reciente tiene IS_CURRENT_VERSION = 1
        [IS_CURRENT_VERSION]    [bit]           NOT NULL
            CONSTRAINT [DF_TRA_FILES_IS_CURRENT] DEFAULT (1),
        [UPLOADED_BY]           [nvarchar](50)  NOT NULL,
        [UPLOADED_DATE]         [datetime2](7)  NOT NULL
            CONSTRAINT [DF_TRA_FILES_DATE] DEFAULT (GETDATE()),

        CONSTRAINT [PK_TRA_DELIVERABLE_FILES] PRIMARY KEY CLUSTERED ([FILE_ID] ASC)
    )
    PRINT 'Tabla TRA_DELIVERABLE_FILES creada.'
END
ELSE
    PRINT 'Tabla TRA_DELIVERABLE_FILES ya existe, se omite.'
GO

-- 2.3 TRA_DELIVERABLE_APPROVALS
-- Una fila por cada revisor requerido por entregable de proyecto.
-- Se pre-crea cuando se instancia el entregable y se actualiza
-- cuando la persona aprueba o rechaza.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TRA_DELIVERABLE_APPROVALS' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[TRA_DELIVERABLE_APPROVALS] (
        [APPROVAL_ID]           [int]           IDENTITY(1,1) NOT NULL,
        [TRA_DELIVERABLE_ID]    [int]           NOT NULL,
        [REVIEWER_USER_ID]      [nvarchar](50)  NOT NULL,
        [APPROVAL_STATUS]       [nchar](10)     NOT NULL
            CONSTRAINT [DF_TRA_APPROVALS_STATUS] DEFAULT ('PENDING'),
        [REVIEWED_DATE]         [datetime2](7)  NULL,
        [APPROVAL_COMMENTS]     [nvarchar](max) NULL,
        [CREATED_DATE]          [datetime2](7)  NOT NULL
            CONSTRAINT [DF_TRA_APPROVALS_CREATED_DATE] DEFAULT (GETDATE()),

        CONSTRAINT [PK_TRA_DELIVERABLE_APPROVALS] PRIMARY KEY CLUSTERED ([APPROVAL_ID] ASC),

        -- Un revisor solo puede tener un registro activo por entregable
        CONSTRAINT [UQ_TRA_APPROVALS_REVIEWER]
            UNIQUE ([TRA_DELIVERABLE_ID], [REVIEWER_USER_ID]),

        CONSTRAINT [CK_TRA_APPROVALS_STATUS]
            CHECK ([APPROVAL_STATUS] IN ('PENDING', 'APPROVED', 'REJECTED'))
    )
    PRINT 'Tabla TRA_DELIVERABLE_APPROVALS creada.'
END
ELSE
    PRINT 'Tabla TRA_DELIVERABLE_APPROVALS ya existe, se omite.'
GO

-- 2.4 TRA_DELIVERABLE_COMMENTS
-- Historial de comentarios por entregable. Inmutable:
-- no se borran ni editan comentarios, solo se agregan.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TRA_DELIVERABLE_COMMENTS' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[TRA_DELIVERABLE_COMMENTS] (
        [COMMENT_ID]            [int]           IDENTITY(1,1) NOT NULL,
        [TRA_DELIVERABLE_ID]    [int]           NOT NULL,
        [COMMENT_TEXT]          [nvarchar](max) NOT NULL,
        [COMMENT_BY]            [nvarchar](50)  NOT NULL,
        [COMMENT_DATE]          [datetime2](7)  NOT NULL
            CONSTRAINT [DF_TRA_COMMENTS_DATE] DEFAULT (GETDATE()),

        CONSTRAINT [PK_TRA_DELIVERABLE_COMMENTS] PRIMARY KEY CLUSTERED ([COMMENT_ID] ASC)
    )
    PRINT 'Tabla TRA_DELIVERABLE_COMMENTS creada.'
END
ELSE
    PRINT 'Tabla TRA_DELIVERABLE_COMMENTS ya existe, se omite.'
GO

-- ============================================================
-- SECCIÓN 3: FOREIGN KEYS
-- ============================================================

-- TRA_DELIVERABLES → MAS_DELIVERABLE_STATUS
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_DELIVERABLES_STATUS')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_DELIVERABLES_STATUS]
        FOREIGN KEY ([DELIVERABLE_STATUS_ID])
        REFERENCES [dbo].[MAS_DELIVERABLE_STATUS] ([DELIVERABLE_STATUS_ID])
    PRINT 'FK FK_TRA_DELIVERABLES_STATUS creada.'
END
GO

-- TRA_DELIVERABLES → MAS_DELIVERABLES (plantilla master)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_DELIVERABLES_MAS_DELIVERABLES')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_DELIVERABLES_MAS_DELIVERABLES]
        FOREIGN KEY ([MODULE_ID], [STATUS_ID], [SGATE_ID], [SEQUENCE])
        REFERENCES [dbo].[MAS_DELIVERABLES] ([MODULE_ID], [STATUS_ID], [SGATE_ID], [SEQUENCE])
    PRINT 'FK FK_TRA_DELIVERABLES_MAS_DELIVERABLES creada.'
END
GO

-- TRA_DELIVERABLE_FILES → TRA_DELIVERABLES
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_FILES_DELIVERABLES')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLE_FILES]
        WITH CHECK ADD CONSTRAINT [FK_TRA_FILES_DELIVERABLES]
        FOREIGN KEY ([TRA_DELIVERABLE_ID])
        REFERENCES [dbo].[TRA_DELIVERABLES] ([TRA_DELIVERABLE_ID])
    PRINT 'FK FK_TRA_FILES_DELIVERABLES creada.'
END
GO

-- TRA_DELIVERABLE_APPROVALS → TRA_DELIVERABLES
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_APPROVALS_DELIVERABLES')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLE_APPROVALS]
        WITH CHECK ADD CONSTRAINT [FK_TRA_APPROVALS_DELIVERABLES]
        FOREIGN KEY ([TRA_DELIVERABLE_ID])
        REFERENCES [dbo].[TRA_DELIVERABLES] ([TRA_DELIVERABLE_ID])
    PRINT 'FK FK_TRA_APPROVALS_DELIVERABLES creada.'
END
GO

-- TRA_DELIVERABLE_COMMENTS → TRA_DELIVERABLES
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TRA_COMMENTS_DELIVERABLES')
BEGIN
    ALTER TABLE [dbo].[TRA_DELIVERABLE_COMMENTS]
        WITH CHECK ADD CONSTRAINT [FK_TRA_COMMENTS_DELIVERABLES]
        FOREIGN KEY ([TRA_DELIVERABLE_ID])
        REFERENCES [dbo].[TRA_DELIVERABLES] ([TRA_DELIVERABLE_ID])
    PRINT 'FK FK_TRA_COMMENTS_DELIVERABLES creada.'
END
GO

-- ============================================================
-- SECCIÓN 4: ÍNDICES
-- ============================================================

-- Búsqueda de todos los entregables de un proyecto
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_DELIVERABLES_OPP_LINE' AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_DELIVERABLES_OPP_LINE]
        ON [dbo].[TRA_DELIVERABLES] ([OPP_LINE_ID])
        INCLUDE ([DELIVERABLE_STATUS_ID], [PLANNED_END_DATE], [ACTUAL_END_DATE])
GO

-- Búsqueda de entregables vencidos/próximos a vencer por proyecto
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_DELIVERABLES_DATES' AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_DELIVERABLES_DATES]
        ON [dbo].[TRA_DELIVERABLES] ([PLANNED_END_DATE], [DELIVERABLE_STATUS_ID])
        INCLUDE ([OPP_LINE_ID], [MODULE_ID], [STATUS_ID], [SGATE_ID])
GO

-- Versión activa de archivos por entregable
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_FILES_CURRENT' AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLE_FILES'))
    CREATE NONCLUSTERED INDEX [IX_TRA_FILES_CURRENT]
        ON [dbo].[TRA_DELIVERABLE_FILES] ([TRA_DELIVERABLE_ID])
        INCLUDE ([FILE_NAME], [FILE_PATH], [FILE_VERSION], [UPLOADED_DATE])
        WHERE [IS_CURRENT_VERSION] = 1
GO

-- Estado de aprobaciones pendientes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_APPROVALS_PENDING' AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLE_APPROVALS'))
    CREATE NONCLUSTERED INDEX [IX_TRA_APPROVALS_PENDING]
        ON [dbo].[TRA_DELIVERABLE_APPROVALS] ([REVIEWER_USER_ID], [APPROVAL_STATUS])
        INCLUDE ([TRA_DELIVERABLE_ID], [CREATED_DATE])
GO

-- Historial cronológico de comentarios
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRA_COMMENTS_CHRONO' AND object_id = OBJECT_ID('dbo.TRA_DELIVERABLE_COMMENTS'))
    CREATE NONCLUSTERED INDEX [IX_TRA_COMMENTS_CHRONO]
        ON [dbo].[TRA_DELIVERABLE_COMMENTS] ([TRA_DELIVERABLE_ID], [COMMENT_DATE] ASC)
GO

-- ============================================================
-- SECCIÓN 5: DATOS INICIALES DE CATÁLOGOS
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM [dbo].[MAS_DELIVERABLE_STATUS])
BEGIN
    INSERT INTO [dbo].[MAS_DELIVERABLE_STATUS]
        ([DELIVERABLE_STATUS_ID], [DELIVERABLE_STATUS_DESC], [STATUS_ORDER], [IS_FINAL_STATE])
    VALUES
        ('PENDING',   'Pendiente',        1, 0),
        ('INPROGRES', 'En Progreso',      2, 0),
        ('SUBMITTED', 'Entregado',        3, 0),
        ('INREVIEW',  'En Revisión',      4, 0),
        ('APPROVED',  'Aprobado',         5, 1),
        ('REJECTED',  'Rechazado',        6, 0),
        ('ONHOLD',    'En Espera',        7, 0),
        ('WAIVED',    'Dispensado',       8, 1)
    PRINT 'Datos iniciales MAS_DELIVERABLE_STATUS insertados.'
END
GO

PRINT '====================================='
PRINT 'Script ejecutado correctamente.'
PRINT '====================================='
GO
