// PropiedadesFichero.cs
// Requiere: .NET 8
// Sin dependencias externas: solo System.IO, System.IO.Compression y System.Xml.Linq
//
// Funciona con:
// - Rutas locales normales.
// - Rutas de carpetas de SharePoint sincronizadas con OneDrive (aparecen como
//   carpetas locales normales en el explorador de Windows, por eso no hace
//   falta ninguna librería/API de SharePoint).
//
// Para documentos Office (.docx, .xlsx, .pptx, .docm, .xlsm, .pptm, etc.)
// se extraen además los metadatos "reales" del documento (autor, quién hizo
// la última modificación, fecha de creación y de última modificación tal
// como constan en el propio fichero), ya que estos formatos son en
// realidad un ZIP que contiene un XML (docProps/core.xml) con esa
// información.

using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace UtilidadesFichero
{
    /// <summary>
    /// Properties of a file: Metadada system files
    /// and, if possible internal metadata from office documents.
    /// </summary>
    public class PropiedadesFicheroInfo
    {
        public string Nombre { get; set; } = string.Empty;
        public string RutaAbsoluta { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }

        // Fechas según el sistema de ficheros local (pueden diferir de las
        // de SharePoint si OneDrive sincronizó el fichero después de crearlo)
        public DateTime FechaCreacionSistema { get; set; }
        public DateTime FechaModificacionSistema { get; set; }
        public DateTime FechaAccesoSistema { get; set; }

        // Metadatos internos del documento (solo si es un OOXML: docx/xlsx/pptx...)
        public string? CreadoPor { get; set; }
        public string? ModificadoPor { get; set; }
        public DateTime? FechaCreacionDoc { get; set; }
        public DateTime? FechaModificacionDoc { get; set; }
        public string? Revision { get; set; }
        public string? Titulo { get; set; }
    }

    public static class ExtractorPropiedadesFichero
    {
        // Extensiones que son en realidad ficheros ZIP con metadatos OOXML
        private static readonly HashSet<string> ExtensionesOoxml = new(StringComparer.OrdinalIgnoreCase)
        {
            ".docx", ".docm", ".dotx", ".dotm",
            ".xlsx", ".xlsm", ".xltx", ".xltm",
            ".pptx", ".pptm", ".potx", ".potm",
        };

        private static readonly XNamespace NsCore =
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        private static readonly XNamespace NsDc = "http://purl.org/dc/elements/1.1/";
        private static readonly XNamespace NsDcTerms = "http://purl.org/dc/terms/";

        /// <summary>
        /// Obtiene las propiedades de un fichero: metadatos del sistema
        /// de ficheros y, si es un documento Office, metadatos internos
        /// (autor, último modificador, fechas del documento, etc.).
        /// </summary>
        /// <param name="path">Ruta al fichero (local o de una carpeta de
        /// SharePoint/OneDrive sincronizada).</param>
        /// <exception cref="FileNotFoundException">Si el fichero no existe o no es accesible.</exception>
        public static PropiedadesFicheroInfo ObtenerPropiedades(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"No se encuentra o no es accesible el fichero: {path}", path);
            }

            var fileInfo = new FileInfo(path);

            var resultado = new PropiedadesFicheroInfo
            {
                Nombre = fileInfo.Name,
                RutaAbsoluta = fileInfo.FullName,
                Extension = fileInfo.Extension.ToLowerInvariant(),
                TamanoBytes = fileInfo.Length,
                FechaCreacionSistema = fileInfo.CreationTime,
                FechaModificacionSistema = fileInfo.LastWriteTime,
                FechaAccesoSistema = fileInfo.LastAccessTime,
            };

            if (ExtensionesOoxml.Contains(resultado.Extension))
            {
                LeerPropiedadesOoxml(path, resultado);
            }

            return resultado;
        }

        /// <summary>
        /// Extrae docProps/core.xml de un fichero Office (OOXML) y rellena
        /// autor, último modificador y fechas de creación/modificación
        /// tal como constan en el propio documento.
        /// Si el fichero no es un OOXML válido o no tiene ese XML, no
        /// rellena esos campos (quedan a null).
        /// </summary>
        private static void LeerPropiedadesOoxml(string path, PropiedadesFicheroInfo destino)
        {
            try
            {
                using var archivoZip = ZipFile.OpenRead(path);
                var entrada = archivoZip.GetEntry("docProps/core.xml");
                if (entrada is null)
                {
                    return;
                }

                using var stream = entrada.Open();
                var documento = XDocument.Load(stream);
                var raiz = documento.Root;
                if (raiz is null)
                {
                    return;
                }

                destino.CreadoPor = raiz.Element(NsDc + "creator")?.Value;
                destino.ModificadoPor = raiz.Element(NsCore + "lastModifiedBy")?.Value;
                destino.Revision = raiz.Element(NsCore + "revision")?.Value;
                destino.Titulo = raiz.Element(NsDc + "title")?.Value;

                var fechaCreacionTexto = raiz.Element(NsDcTerms + "created")?.Value;
                if (DateTime.TryParse(fechaCreacionTexto, out var fechaCreacion))
                {
                    destino.FechaCreacionDoc = fechaCreacion;
                }

                var fechaModificacionTexto = raiz.Element(NsDcTerms + "modified")?.Value;
                if (DateTime.TryParse(fechaModificacionTexto, out var fechaModificacion))
                {
                    destino.FechaModificacionDoc = fechaModificacion;
                }
            }
            catch (InvalidDataException)
            {
                // No es un ZIP/OOXML válido: se ignora, quedan los campos a null
            }
        }
    }

    // Ejemplo de uso:
    //
    // var props = ExtractorPropiedadesFichero.ObtenerPropiedades(
    //     @"C:\Users\usuario\OneDrive - Empresa\Documentos\Informe.docx");
    //
    // Console.WriteLine($"Nombre: {props.Nombre}");
    // Console.WriteLine($"Creado por: {props.CreadoPor}");
    // Console.WriteLine($"Modificado por: {props.ModificadoPor}");
    // Console.WriteLine($"Fecha creación (doc): {props.FechaCreacionDoc}");
    // Console.WriteLine($"Fecha modificación (doc): {props.FechaModificacionDoc}");
}