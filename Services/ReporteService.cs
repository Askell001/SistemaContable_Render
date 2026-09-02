using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using SistemaContable.Models;

namespace SistemaContable.Services
{
    /// <summary>
    /// Servicio para la generación de reportes contables en memoria (Excel, PDF y XML)
    /// con metadatos de hora de Ecuador (UTC-5) para descarga directa en el navegador.
    /// </summary>
    public class ReporteService
    {
        /// <summary>
        /// Obtiene la fecha y hora actual en la zona horaria de Ecuador (SA Pacific Standard Time / UTC-5).
        /// </summary>
        public static DateTime ObtenerHoraEcuador()
        {
            try
            {
                var tzEcuador = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzEcuador);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(-5);
            }
        }

        // =========================================================================
        // 1. GENERACIÓN DE REPORTE EXCEL (ClosedXML)
        // =========================================================================
        public byte[] GenerarExcel(FiltroReporteViewModel model)
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Asientos Contables");
                ws.ShowGridLines = true;

                // 1. Título y Encabezado de Empresa
                ws.Cell("A1").Value = "SISTEMA CONTABLE CORPORATIVO - REPORTES DE PÓLIZAS";
                ws.Cell("A1").Style.Font.Bold = true;
                ws.Cell("A1").Style.Font.FontSize = 15;
                ws.Cell("A1").Style.Font.FontColor = XLColor.White;
                ws.Range("A1:G1").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#198754");
                ws.Range("A1:G1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Range("A1:G1").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Height = 30;

                // 2. Metadatos del Reporte (Zona Horaria Ecuador y Usuario)
                ws.Cell("A3").Value = "Fecha y Hora Generación (Ecuador):";
                ws.Cell("B3").Value = model.FechaGeneracionEC.ToString("dd/MM/yyyy HH:mm:ss") + " (UTC-5)";
                ws.Cell("A3").Style.Font.Bold = true;

                ws.Cell("D3").Value = "Usuario Generador:";
                ws.Cell("E3").Value = $"{model.UsuarioNombre} ({model.UsuarioCorreo})";
                ws.Cell("D3").Style.Font.Bold = true;

                ws.Cell("A4").Value = "Rango de Fechas Filtro:";
                string rangoTexto = (model.FechaInicio.HasValue ? model.FechaInicio.Value.ToString("dd/MM/yyyy") : "Inicio") +
                                    " hasta " +
                                    (model.FechaFin.HasValue ? model.FechaFin.Value.ToString("dd/MM/yyyy") : "Actualidad");
                ws.Cell("B4").Value = rangoTexto;
                ws.Cell("A4").Style.Font.Bold = true;

                ws.Cell("D4").Value = "Empresa / Filtro Estado:";
                ws.Cell("E4").Value = $"{model.Empresa} | {model.Estado}";
                ws.Cell("D4").Style.Font.Bold = true;

                int row = 6;

                // 3. Cabecera de Columnas
                string[] headers = { "N° Asiento", "Fecha", "Concepto / Glosa", "Estado", "Código Cuenta", "Nombre Cuenta", "Debe ($)", "Haber ($)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(row, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#212529");
                    cell.Style.Alignment.Horizontal = (i >= 6) ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left;
                }
                ws.Row(row).Height = 22;
                row++;

                decimal granTotalDebe = 0;
                decimal granTotalHaber = 0;

                // 4. Filas de Asientos y Detalles
                foreach (var a in model.Asientos)
                {
                    bool primeraFila = true;
                    int startAsientoRow = row;

                    foreach (var d in a.Detalles)
                    {
                        if (primeraFila)
                        {
                            ws.Cell(row, 1).Value = "#" + a.NumeroAsiento;
                            ws.Cell(row, 2).Value = a.Fecha.ToString("dd/MM/yyyy");
                            ws.Cell(row, 3).Value = a.Concepto;
                            ws.Cell(row, 4).Value = a.Estado;

                            var estadoCell = ws.Cell(row, 4);
                            estadoCell.Style.Font.Bold = true;
                            if (a.Estado == "Aprobado") estadoCell.Style.Font.FontColor = XLColor.FromHtml("#198754");
                            else if (a.Estado == "Anulado") estadoCell.Style.Font.FontColor = XLColor.FromHtml("#dc3545");
                            else estadoCell.Style.Font.FontColor = XLColor.FromHtml("#ffc107");

                            primeraFila = false;
                        }

                        ws.Cell(row, 5).Value = d.CuentaCodigo;
                        ws.Cell(row, 6).Value = d.CuentaNombre;
                        ws.Cell(row, 7).Value = d.Debe;
                        ws.Cell(row, 7).Style.NumberFormat.Format = "$#,##0.00";
                        ws.Cell(row, 8).Value = d.Haber;
                        ws.Cell(row, 8).Style.NumberFormat.Format = "$#,##0.00";

                        granTotalDebe += d.Debe;
                        granTotalHaber += d.Haber;
                        row++;
                    }

                    // Línea separadora suave por asiento
                    ws.Range(startAsientoRow, 1, row - 1, 8).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    ws.Range(startAsientoRow, 1, row - 1, 8).Style.Border.InsideBorderColor = XLColor.FromHtml("#e9ecef");
                    ws.Range(startAsientoRow, 1, row - 1, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Range(startAsientoRow, 1, row - 1, 8).Style.Border.OutsideBorderColor = XLColor.FromHtml("#ced4da");
                }

                // 5. Fila de Gran Total
                ws.Cell(row, 1).Value = "SUMAS TOTALES:";
                ws.Range(row, 1, row, 6).Merge().Style.Font.Bold = true;
                ws.Range(row, 1, row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f9fa");

                ws.Cell(row, 7).Value = granTotalDebe;
                ws.Cell(row, 7).Style.Font.Bold = true;
                ws.Cell(row, 7).Style.NumberFormat.Format = "$#,##0.00";
                ws.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

                ws.Cell(row, 8).Value = granTotalHaber;
                ws.Cell(row, 8).Style.Font.Bold = true;
                ws.Cell(row, 8).Style.NumberFormat.Format = "$#,##0.00";
                ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

                ws.Range(row, 1, row, 8).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                ws.Range(row, 1, row, 8).Style.Border.BottomBorder = XLBorderStyleValues.Double;

                ws.Columns().AdjustToContents();
                ws.Column(3).Width = Math.Max(ws.Column(3).Width, 28);
                ws.Column(6).Width = Math.Max(ws.Column(6).Width, 30);

                using (var ms = new MemoryStream())
                {
                    workbook.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        // =========================================================================
        // 2. GENERACIÓN DE REPORTE PDF (iTextSharp)
        // =========================================================================
        public byte[] GenerarPdf(FiltroReporteViewModel model)
        {
            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                var writer = PdfWriter.GetInstance(doc, ms);

                // Pie de página con numeración y metadatos
                writer.PageEvent = new PdfReporteFooter(model.FechaGeneracionEC, model.UsuarioCorreo);

                doc.Open();

                // Colores Corporativos
                var colorHeader = new BaseColor(25, 135, 84); // Success green
                var colorDark = new BaseColor(33, 37, 41);
                var colorGris = new BaseColor(245, 247, 250);
                var colorGrisBorde = new BaseColor(220, 224, 230);

                var fontTitulo = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.WHITE);
                var fontSub = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.WHITE);
                var fontMetaLbl = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, colorDark);
                var fontMetaVal = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.DARK_GRAY);
                var fontTh = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, BaseColor.WHITE);
                var fontTd = FontFactory.GetFont(FontFactory.HELVETICA, 8, colorDark);
                var fontTdBold = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, colorDark);

                // 1. Encabezado Principal
                var tablaHeader = new PdfPTable(1) { WidthPercentage = 100 };
                var cellH = new PdfPCell
                {
                    BackgroundColor = colorHeader,
                    Padding = 10,
                    Border = Rectangle.NO_BORDER
                };
                cellH.AddElement(new Paragraph("SISTEMA CONTABLE CORPORATIVO - REPORTE DE ASIENTOS", fontTitulo));
                cellH.AddElement(new Paragraph($"Control Integral de Partida Doble y Auditoría Contable | Empresa: {model.Empresa}", fontSub));
                tablaHeader.AddCell(cellH);
                doc.Add(tablaHeader);

                doc.Add(new Paragraph(" "));

                // 2. Metadatos de Generación (Ecuador UTC-5 y Usuario)
                var tablaMeta = new PdfPTable(4) { WidthPercentage = 100 };
                tablaMeta.SetWidths(new float[] { 22, 28, 20, 30 });

                void AgregarMeta(string lbl, string val)
                {
                    var c1 = new PdfPCell(new Phrase(lbl, fontMetaLbl)) { Border = Rectangle.NO_BORDER, Padding = 2 };
                    var c2 = new PdfPCell(new Phrase(val, fontMetaVal)) { Border = Rectangle.NO_BORDER, Padding = 2 };
                    tablaMeta.AddCell(c1);
                    tablaMeta.AddCell(c2);
                }

                AgregarMeta("Fecha/Hora (Ecuador):", model.FechaGeneracionEC.ToString("dd/MM/yyyy HH:mm:ss") + " (UTC-5)");
                AgregarMeta("Usuario Generador:", $"{model.UsuarioNombre}");
                
                string rango = (model.FechaInicio.HasValue ? model.FechaInicio.Value.ToString("dd/MM/yyyy") : "Inicio") +
                               " a " +
                               (model.FechaFin.HasValue ? model.FechaFin.Value.ToString("dd/MM/yyyy") : "Actualidad");
                AgregarMeta("Rango de Fechas:", rango);
                AgregarMeta("Empresa Asignada:", model.Empresa);

                AgregarMeta("Estado Filtrado:", model.Estado);
                AgregarMeta("Total Asientos:", model.TotalAsientos.ToString());

                doc.Add(tablaMeta);
                doc.Add(new Paragraph(" "));

                // 3. Tabla Principal de Asientos y Movimientos
                var tabla = new PdfPTable(8) { WidthPercentage = 100 };
                tabla.SetWidths(new float[] { 9, 10, 24, 10, 11, 20, 8, 8 });

                string[] ths = { "N° Asiento", "Fecha", "Concepto / Glosa", "Estado", "Código", "Cuenta Contable", "Debe ($)", "Haber ($)" };
                foreach (var th in ths)
                {
                    var c = new PdfPCell(new Phrase(th, fontTh))
                    {
                        BackgroundColor = colorDark,
                        HorizontalAlignment = (th.Contains("($)")) ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT,
                        Padding = 5,
                        BorderColor = colorDark
                    };
                    tabla.AddCell(c);
                }

                decimal totalDebe = 0;
                decimal totalHaber = 0;
                bool toggleColor = false;

                foreach (var a in model.Asientos)
                {
                    bool primero = true;
                    var bgAsiento = toggleColor ? colorGris : BaseColor.WHITE;
                    toggleColor = !toggleColor;

                    foreach (var d in a.Detalles)
                    {
                        var cellNum = new PdfPCell(new Phrase(primero ? "#" + a.NumeroAsiento : "", fontTdBold)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellFec = new PdfPCell(new Phrase(primero ? a.Fecha.ToString("dd/MM/yyyy") : "", fontTd)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellCon = new PdfPCell(new Phrase(primero ? a.Concepto : "", fontTd)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellEst = new PdfPCell(new Phrase(primero ? a.Estado : "", fontTdBold)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };

                        var cellCod = new PdfPCell(new Phrase(d.CuentaCodigo, fontTd)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellNom = new PdfPCell(new Phrase(d.CuentaNombre, fontTd)) { BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellDeb = new PdfPCell(new Phrase(d.Debe.ToString("N2"), fontTd)) { HorizontalAlignment = Element.ALIGN_RIGHT, BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };
                        var cellHab = new PdfPCell(new Phrase(d.Haber.ToString("N2"), fontTd)) { HorizontalAlignment = Element.ALIGN_RIGHT, BackgroundColor = bgAsiento, Padding = 4, BorderColor = colorGrisBorde };

                        tabla.AddCell(cellNum);
                        tabla.AddCell(cellFec);
                        tabla.AddCell(cellCon);
                        tabla.AddCell(cellEst);
                        tabla.AddCell(cellCod);
                        tabla.AddCell(cellNom);
                        tabla.AddCell(cellDeb);
                        tabla.AddCell(cellHab);

                        totalDebe += d.Debe;
                        totalHaber += d.Haber;
                        primero = false;
                    }
                }

                // Fila de Sumas Totales
                var cellTotLbl = new PdfPCell(new Phrase("SUMAS TOTALES:", fontTdBold))
                {
                    Colspan = 6,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    BackgroundColor = new BaseColor(235, 245, 235),
                    Padding = 6,
                    BorderColor = colorDark
                };
                var cellTotDeb = new PdfPCell(new Phrase(totalDebe.ToString("N2"), fontTdBold))
                {
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    BackgroundColor = new BaseColor(235, 245, 235),
                    Padding = 6,
                    BorderColor = colorDark
                };
                var cellTotHab = new PdfPCell(new Phrase(totalHaber.ToString("N2"), fontTdBold))
                {
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    BackgroundColor = new BaseColor(235, 245, 235),
                    Padding = 6,
                    BorderColor = colorDark
                };

                tabla.AddCell(cellTotLbl);
                tabla.AddCell(cellTotDeb);
                tabla.AddCell(cellTotHab);

                doc.Add(tabla);
                doc.Close();

                return ms.ToArray();
            }
        }

        // =========================================================================
        // 3. GENERACIÓN DE REPORTE XML (XDocument)
        // =========================================================================
        public byte[] GenerarXml(FiltroReporteViewModel model)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("ReporteContable",
                    new XElement("MetaDatos",
                        new XElement("FechaGeneracionEC", model.FechaGeneracionEC.ToString("yyyy-MM-dd HH:mm:ss")),
                        new XElement("ZonaHoraria", "SA Pacific Standard Time (UTC-5 - Ecuador)"),
                        new XElement("Empresa", model.Empresa ?? "Empresa Principal S.A."),
                        new XElement("UsuarioGenerador",
                            new XElement("Nombre", model.UsuarioNombre ?? string.Empty),
                            new XElement("Correo", model.UsuarioCorreo ?? string.Empty)
                        ),
                        new XElement("FiltrosAplicados",
                            new XElement("FechaInicio", model.FechaInicio.HasValue ? model.FechaInicio.Value.ToString("yyyy-MM-dd") : "Todos"),
                            new XElement("FechaFin", model.FechaFin.HasValue ? model.FechaFin.Value.ToString("yyyy-MM-dd") : "Todos"),
                            new XElement("Estado", model.Estado ?? "Todos")
                        )
                    ),
                    new XElement("Resumen",
                        new XElement("TotalAsientos", model.TotalAsientos),
                        new XElement("GranTotalDebe", model.TotalDebeGlobal.ToString("F2")),
                        new XElement("GranTotalHaber", model.TotalHaberGlobal.ToString("F2")),
                        new XElement("Cuadrado", Math.Abs(model.TotalDebeGlobal - model.TotalHaberGlobal) < 0.001m ? "SI" : "NO")
                    ),
                    new XElement("AsientosContables",
                        model.Asientos.Select(a =>
                            new XElement("Asiento",
                                new XAttribute("id", a.Id ?? string.Empty),
                                new XElement("NumeroAsiento", a.NumeroAsiento),
                                new XElement("Fecha", a.Fecha.ToString("yyyy-MM-dd")),
                                new XElement("Concepto", a.Concepto ?? string.Empty),
                                new XElement("Estado", a.Estado ?? string.Empty),
                                new XElement("UsuarioCreador",
                                    new XElement("Nombre", a.UsuarioCreadorNombre ?? string.Empty),
                                    new XElement("Correo", a.UsuarioCreadorCorreo ?? string.Empty)
                                ),
                                new XElement("TotalDebe", a.TotalDebe.ToString("F2")),
                                new XElement("TotalHaber", a.TotalHaber.ToString("F2")),
                                new XElement("Movimientos",
                                    a.Detalles.Select(d =>
                                        new XElement("Movimiento",
                                            new XElement("CuentaCodigo", d.CuentaCodigo ?? string.Empty),
                                            new XElement("CuentaNombre", d.CuentaNombre ?? string.Empty),
                                            new XElement("CuentaTipo", d.CuentaTipo ?? string.Empty),
                                            new XElement("Debe", d.Debe.ToString("F2")),
                                            new XElement("Haber", d.Haber.ToString("F2"))
                                        )
                                    )
                                )
                            )
                        )
                    )
                )
            );

            using (var ms = new MemoryStream())
            {
                using (var writer = new System.Xml.XmlTextWriter(ms, Encoding.UTF8) { Formatting = System.Xml.Formatting.Indented })
                {
                    doc.Save(writer);
                }
                return ms.ToArray();
            }
        }

        #region Helper Evento Footer PDF
        private class PdfReporteFooter : PdfPageEventHelper
        {
            private readonly DateTime _fechaEC;
            private readonly string _usuario;

            public PdfReporteFooter(DateTime fechaEC, string usuario)
            {
                _fechaEC = fechaEC;
                _usuario = usuario;
            }

            public override void OnEndPage(PdfWriter writer, Document document)
            {
                var fontFooter = FontFactory.GetFont(FontFactory.HELVETICA, 7, BaseColor.GRAY);
                var cb = writer.DirectContent;
                string texto = $"Generado el {_fechaEC:dd/MM/yyyy HH:mm:ss} (Ecuador UTC-5) por {_usuario} | Página {writer.PageNumber}";
                ColumnText.ShowTextAligned(cb, Element.ALIGN_CENTER, new Phrase(texto, fontFooter), (document.Right + document.Left) / 2, document.Bottom - 15, 0);
            }
        }
        #endregion
    }
}
