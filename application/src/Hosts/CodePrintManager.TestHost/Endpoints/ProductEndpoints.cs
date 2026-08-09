using CodePrintManager.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using CodePrintManager.Data;
using CodePrintManager.Domain.Entities;

namespace CodePrintManager.TestHost.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var products = await db.ProductNodes.ToListAsync();
            return Results.Ok(products.Select(p => new
            {
                p.Id, p.Name, p.IsLeaf, p.ParentId, p.TemplateFile, p.PrinterCsvName
            }));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, ICodePoolService codePool) =>
        {
            var product = await db.ProductNodes.FindAsync(id);
            if (product == null) return Results.NotFound();

            var stats = product.IsLeaf ? await codePool.GetPoolStatsAsync(id) : null;
            return Results.Ok(new
            {
                product.Id, product.Name, product.IsLeaf, product.ParentId,
                product.TemplateFile, product.PrinterCsvName,
                PoolStats = stats?.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            });
        });

        group.MapPost("/", async (CreateProductRequest req, IProductService productService) =>
        {
            ProductNode product;
            if (req.IsLeaf == false)
            {
                product = await productService.CreateFolderAsync(req.Name, req.ParentId);
            }
            else
            {
                product = await productService.CreateProductAsync(
                    req.Name, req.ParentId, req.TemplateFile ?? "default.rox", req.CsvName ?? "data.csv");
            }
            return Results.Created($"/api/products/{product.Id}", new { product.Id, product.Name });
        });

        group.MapPut("/{id:int}", async (int id, UpdateProductRequest req, AppDbContext db) =>
        {
            var product = await db.ProductNodes.FindAsync(id);
            if (product == null) return Results.NotFound();

            if (req.Name != null) product.Name = req.Name;
            if (req.TemplateFile != null) product.TemplateFile = req.TemplateFile;
            if (req.CsvName != null) product.PrinterCsvName = req.CsvName;
            await db.SaveChangesAsync();
            return Results.Ok(new { product.Id, product.Name });
        });

        group.MapDelete("/{id:int}", async (int id, IProductService productService) =>
        {
            try
            {
                await productService.DeleteAsync(id);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { Error = ex.Message });
            }
        });

        group.MapPost("/{id:int}/import-csv", async (int id, ImportCsvRequest req, ICodePoolService codePool) =>
        {
            var result = await codePool.ImportCodesAsync(id, req.BatchName ?? "api-import", req.Codes);
            return Results.Ok(new { result.Imported, result.Duplicates, result.Errors });
        });
    }
}

public record CreateProductRequest(string Name, bool? IsLeaf = true, int? ParentId = null, string? TemplateFile = null, string? CsvName = null);
public record UpdateProductRequest(string? Name = null, string? TemplateFile = null, string? CsvName = null);
public record ImportCsvRequest(List<string> Codes, string? BatchName = null);
