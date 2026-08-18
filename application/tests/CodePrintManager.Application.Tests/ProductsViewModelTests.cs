using CodePrintManager.Data;
using CodePrintManager.Desktop.ViewModels;
using CodePrintManager.Domain.Entities;
using CodePrintManager.Domain.Enums;
using CodePrintManager.Domain.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PrinterEntity = CodePrintManager.Domain.Entities.Printer;

namespace CodePrintManager.Application.Tests;

/// <summary>
/// Comprehensive unit tests for the Products page ViewModel.
/// Tests are requirement-driven, based on products-page-design.md.
///
/// Test areas:
///   1. Tree Loading & Initial State
///   2. Selection Behavior & Detail Pane
///   3. Add Folder — Parent Resolution Logic (Section 3.3)
///   4. Add Product — Parent Resolution Logic (Section 3.4)
///   5. Activity History — Merged Timeline (Section 4b)
///   6. Code Pool Stats Refresh
///   7. Delete Guards (Section 3.5)
///   8. New Job Navigation (Section 3.2)
///   9. Deselect & Root-Level Creation (Section 2.2 / 3.3)
///  10. Edge Cases & Validation
/// </summary>
public class ProductsViewModelTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IProductService _productService;
    private readonly ICodePoolService _codePoolService;
    private readonly ICodeManagementService _codeManagement;
    private readonly ILogger<ProductsViewModel> _logger;
    private readonly ProductsViewModel _vm;

    public ProductsViewModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _productService = Substitute.For<IProductService>();
        _codePoolService = Substitute.For<ICodePoolService>();
        _codeManagement = Substitute.For<ICodeManagementService>();
        _logger = Substitute.For<ILogger<ProductsViewModel>>();

        var codesTabLogger = Substitute.For<ILogger<CodesTabViewModel>>();
        var codesTab = new CodesTabViewModel(_codeManagement, _productService, codesTabLogger);
        _vm = new ProductsViewModel(_productService, _codePoolService, _codeManagement, _db, codesTab, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. TREE LOADING & INITIAL STATE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadProducts_PopulatesTreeFromService()
    {
        // Requirement: Page loads tree from IProductService.GetRootsAsync()
        var roots = new List<ProductNode>
        {
            new() { Id = 1, Name = "Juice", IsLeaf = false, Children = new List<ProductNode>
            {
                new() { Id = 2, Name = "Apple 0.5L", IsLeaf = true, ParentId = 1 }
            }},
            new() { Id = 3, Name = "Water", IsLeaf = false }
        };
        _productService.GetRootsAsync().Returns(roots);

        await _vm.LoadProductsCommand.ExecuteAsync(null);

        _vm.Products.Should().HaveCount(2);
        _vm.Products[0].Name.Should().Be("Juice");
        _vm.Products[1].Name.Should().Be("Water");
    }

    [Fact]
    public void InitialState_NoSelection_DetailPaneEmpty()
    {
        // Requirement: On first load with no selection, detail pane has nothing
        _vm.SelectedProduct.Should().BeNull();
        _vm.AvailableCodesCount.Should().Be(0);
        _vm.PrintedCodesCount.Should().Be(0);
        _vm.BurnedCodesCount.Should().Be(0);
        _vm.TotalCodesCount.Should().Be(0);
        _vm.ActivityHistory.Should().BeEmpty();
    }

    [Fact]
    public void InitialState_AddTargetHint_ShowsRoot()
    {
        // Requirement: When nothing selected, adding creates at root
        _vm.AddTargetHint.Should().Be("Root");
    }

    [Fact]
    public void InitialState_AddingFlags_AreFalse()
    {
        _vm.IsAddingFolder.Should().BeFalse();
        _vm.IsAddingProduct.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. SELECTION BEHAVIOR & DETAIL PANE
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SelectLeafProduct_LoadsCodePoolStats()
    {
        // Requirement: Selecting a leaf product shows code pool stats
        var product = new ProductNode { Id = 5, Name = "Apple 0.5L", IsLeaf = true };
        var stats = new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 8300 },
            { CodeStatus.Printed, 1700 },
            { CodeStatus.Burned, 3 }
        };
        _codePoolService.GetPoolStatsAsync(5).Returns(stats);

        _vm.SelectedProduct = product;

        // Wait for async refresh
        await Task.Delay(100);

        _vm.AvailableCodesCount.Should().Be(8300);
        _vm.PrintedCodesCount.Should().Be(1700);
        _vm.BurnedCodesCount.Should().Be(3);
        _vm.TotalCodesCount.Should().Be(10003);
    }

    [Fact]
    public async Task SelectFolder_ClearsCodePoolStats()
    {
        // Requirement: Selecting a folder shows no code stats (it's not a leaf)
        var folder = new ProductNode { Id = 1, Name = "Juice", IsLeaf = false };

        _vm.SelectedProduct = folder;
        await Task.Delay(100);

        _vm.AvailableCodesCount.Should().Be(0);
        _vm.PrintedCodesCount.Should().Be(0);
        _vm.BurnedCodesCount.Should().Be(0);
        _vm.TotalCodesCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectNull_ClearsEverything()
    {
        // Setup: first select something
        var product = new ProductNode { Id = 5, Name = "Test", IsLeaf = true };
        _codePoolService.GetPoolStatsAsync(5).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 100 }
        });
        _vm.SelectedProduct = product;
        await Task.Delay(100);

        // Now deselect
        _vm.SelectedProduct = null;
        await Task.Delay(100);

        _vm.AvailableCodesCount.Should().Be(0);
        _vm.ActivityHistory.Should().BeEmpty();
        _vm.AddTargetHint.Should().Be("Root");
    }

    [Fact]
    public void SelectFolder_AddTargetHint_ShowsFolderName()
    {
        // Requirement: When folder selected, adding creates CHILD of that folder
        var folder = new ProductNode { Id = 1, Name = "Juice", IsLeaf = false };
        _vm.SelectedProduct = folder;

        _vm.AddTargetHint.Should().Be("Juice");
    }

    [Fact]
    public void SelectLeaf_AddTargetHint_ShowsParentNameOrRoot()
    {
        // Requirement: When leaf selected, adding creates SIBLING (parent context)
        var parent = new ProductNode { Id = 1, Name = "Juice", IsLeaf = false };
        var leaf = new ProductNode { Id = 2, Name = "Apple 0.5L", IsLeaf = true, ParentId = 1, Parent = parent };

        _vm.SelectedProduct = leaf;

        _vm.AddTargetHint.Should().Be("Juice");
    }

    [Fact]
    public void SelectRootLeaf_AddTargetHint_ShowsRoot()
    {
        // A leaf with no parent → adding creates sibling at root
        var leaf = new ProductNode { Id = 2, Name = "Standalone", IsLeaf = true, ParentId = null, Parent = null };

        _vm.SelectedProduct = leaf;

        _vm.AddTargetHint.Should().Be("Root");
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. ADD FOLDER — PARENT RESOLUTION LOGIC (Section 3.3)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddFolder_NothingSelected_CreatesAtRoot()
    {
        // Requirement: Nothing selected → parentId = null → root level
        _vm.SelectedProduct = null;
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "New Root Folder";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("New Root Folder", null);
    }

    [Fact]
    public async Task AddFolder_FolderSelected_CreatesAsChild()
    {
        // Requirement: Folder selected → creates CHILD of that folder
        var folder = new ProductNode { Id = 10, Name = "Juice", IsLeaf = false };
        _vm.SelectedProduct = folder;
        await Task.Delay(50);
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "Apple";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("Apple", 10);
    }

    [Fact]
    public async Task AddFolder_LeafSelected_CreatesAsSibling()
    {
        // Requirement: Leaf selected → parentId = leaf's ParentId → sibling
        var leaf = new ProductNode { Id = 5, Name = "Apple 0.5L", IsLeaf = true, ParentId = 10 };
        _vm.SelectedProduct = leaf;
        await Task.Delay(50);
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "New Sibling Folder";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("New Sibling Folder", 10);
    }

    [Fact]
    public async Task AddFolder_EmptyName_DoesNotCreate()
    {
        // Requirement: Empty/whitespace name → rejected
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "   ";

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.DidNotReceive().CreateFolderAsync(Arg.Any<string>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task AddFolder_NameIsTrimmed()
    {
        // Requirement: Whitespace around name is trimmed
        _vm.SelectedProduct = null;
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "  Beverages  ";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("Beverages", null);
    }

    [Fact]
    public void ShowAddFolder_HidesAddProductForm()
    {
        // Requirement: Only one add form visible at a time
        _vm.IsAddingProduct = true;
        _vm.ShowAddFolderCommand.Execute(null);

        _vm.IsAddingFolder.Should().BeTrue();
        _vm.IsAddingProduct.Should().BeFalse();
    }

    [Fact]
    public void CancelAdd_HidesBothForms()
    {
        _vm.IsAddingFolder = true;
        _vm.CancelAddCommand.Execute(null);

        _vm.IsAddingFolder.Should().BeFalse();
        _vm.IsAddingProduct.Should().BeFalse();
    }

    [Fact]
    public async Task AddFolder_AfterCreate_FormCloses_TreeRefreshes()
    {
        // Requirement: After creation, form closes and tree refreshes
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "NewFolder";
        _productService.GetRootsAsync().Returns(new List<ProductNode>
        {
            new() { Id = 99, Name = "NewFolder", IsLeaf = false }
        });

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        _vm.IsAddingFolder.Should().BeFalse();
        _vm.NewNodeName.Should().BeEmpty();
        await _productService.Received(1).GetRootsAsync(); // tree refreshed
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. ADD PRODUCT — PARENT RESOLUTION LOGIC (Section 3.4)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddProduct_NothingSelected_CreatesAtRoot()
    {
        _vm.SelectedProduct = null;
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "Standalone Product";
        _vm.NewProductTemplate = "standalone.rox";
        _vm.NewProductCsvName = "standalone.csv";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateProductAsync("Standalone Product", null, "standalone.rox", "standalone.csv");
    }

    [Fact]
    public async Task AddProduct_FolderSelected_CreatesAsChild()
    {
        var folder = new ProductNode { Id = 10, Name = "Juice", IsLeaf = false };
        _vm.SelectedProduct = folder;
        await Task.Delay(50);
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "Orange 1L";
        _vm.NewProductTemplate = "orange.rox";
        _vm.NewProductCsvName = "orange_1l.csv";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateProductAsync("Orange 1L", 10, "orange.rox", "orange_1l.csv");
    }

    [Fact]
    public async Task AddProduct_LeafSelected_CreatesAsSibling()
    {
        var leaf = new ProductNode { Id = 5, Name = "Apple 0.5L", IsLeaf = true, ParentId = 10 };
        _vm.SelectedProduct = leaf;
        await Task.Delay(50);
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "Apple 1L";
        _vm.NewProductTemplate = "apple.rox";
        _vm.NewProductCsvName = "apple_1l.csv";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateProductAsync("Apple 1L", 10, "apple.rox", "apple_1l.csv");
    }

    [Fact]
    public async Task AddProduct_EmptyName_DoesNotCreate()
    {
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "";
        _vm.NewProductTemplate = "t.rox";
        _vm.NewProductCsvName = "t.csv";

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.DidNotReceive().CreateProductAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AddProduct_AfterCreate_ClearsAllFields()
    {
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "Test";
        _vm.NewProductTemplate = "test.rox";
        _vm.NewProductCsvName = "test.csv";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        _vm.IsAddingProduct.Should().BeFalse();
        _vm.NewNodeName.Should().BeEmpty();
        _vm.NewProductTemplate.Should().BeEmpty();
        _vm.NewProductCsvName.Should().BeEmpty();
    }

    [Fact]
    public void ShowAddProduct_HidesAddFolderForm()
    {
        _vm.IsAddingFolder = true;
        _vm.ShowAddProductCommand.Execute(null);

        _vm.IsAddingProduct.Should().BeTrue();
        _vm.IsAddingFolder.Should().BeFalse();
    }

    [Fact]
    public void ShowAddProduct_ClearsFields()
    {
        // Requirement: Opening form resets all fields
        _vm.NewNodeName = "stale";
        _vm.NewProductTemplate = "stale.rox";
        _vm.NewProductCsvName = "stale.csv";

        _vm.ShowAddProductCommand.Execute(null);

        _vm.NewNodeName.Should().BeEmpty();
        _vm.NewProductTemplate.Should().BeEmpty();
        _vm.NewProductCsvName.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. ACTIVITY HISTORY — MERGED TIMELINE (Section 4b)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActivityHistory_LoadsImportEvents()
    {
        // Seed DB with import audit entries
        var product = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        _db.AuditLog.Add(new AuditEntry
        {
            EventType = "import",
            ProductId = 1,
            Details = "Imported 5,000 codes — batch1.csv",
            CreatedAt = new DateTime(2026, 8, 6, 9, 0, 0)
        });
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = product;
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Type.Should().Be(ActivityType.Import);
        _vm.ActivityHistory[0].Description.Should().Contain("batch1.csv");
    }

    [Fact]
    public async Task ActivityHistory_LoadsCompletedJobsWithPrintedCodes()
    {
        // Requirement: Only jobs with CodesConfirmed > 0 appear
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.AddRange(
            new PrintJob
            {
                Id = 100, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Completed, CodesConfirmed = 500,
                CompletedAt = new DateTime(2026, 8, 10, 14, 30, 0),
                CreatedAt = new DateTime(2026, 8, 10, 14, 0, 0)
            },
            new PrintJob
            {
                Id = 101, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Cancelled, CodesConfirmed = 200,
                CompletedAt = new DateTime(2026, 8, 8, 16, 45, 0),
                CreatedAt = new DateTime(2026, 8, 8, 16, 0, 0)
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        var product = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(2);
        _vm.ActivityHistory[0].Type.Should().Be(ActivityType.JobCompleted);
        _vm.ActivityHistory[0].Description.Should().Contain("500/500");
        _vm.ActivityHistory[1].Type.Should().Be(ActivityType.JobCancelled);
        _vm.ActivityHistory[1].Description.Should().Contain("200/500");
    }

    [Fact]
    public async Task ActivityHistory_ExcludesJobsWithZeroPrintedCodes()
    {
        // Requirement: Jobs cancelled before printing (CodesConfirmed == 0) are excluded
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.AddRange(
            new PrintJob
            {
                Id = 100, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Cancelled, CodesConfirmed = 0, // <-- excluded
                CreatedAt = new DateTime(2026, 8, 7, 10, 0, 0)
            },
            new PrintJob
            {
                Id = 101, ProductId = 1, PrinterId = 1, Quantity = 100,
                Status = JobStatus.Completed, CodesConfirmed = 100,
                CompletedAt = new DateTime(2026, 8, 8, 12, 0, 0),
                CreatedAt = new DateTime(2026, 8, 8, 11, 0, 0)
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Description.Should().Contain("100/100");
    }

    [Fact]
    public async Task ActivityHistory_ExcludesActiveJobs()
    {
        // Requirement: Only completed/cancelled/error appear, not Printing/Ready/Paused
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.AddRange(
            new PrintJob
            {
                Id = 100, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Printing, CodesConfirmed = 50,
                CreatedAt = new DateTime(2026, 8, 10, 14, 0, 0)
            },
            new PrintJob
            {
                Id = 101, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Paused, CodesConfirmed = 200,
                CreatedAt = new DateTime(2026, 8, 9, 14, 0, 0)
            },
            new PrintJob
            {
                Id = 102, ProductId = 1, PrinterId = 1, Quantity = 500,
                Status = JobStatus.Ready, CodesConfirmed = 0,
                CreatedAt = new DateTime(2026, 8, 8, 14, 0, 0)
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivityHistory_MergedAndSortedByDateDescending()
    {
        // Requirement: Both imports and jobs merged, sorted newest first
        _db.AuditLog.AddRange(
            new AuditEntry
            {
                EventType = "import", ProductId = 1,
                Details = "Imported 5,000 — batch_aug6.csv",
                CreatedAt = new DateTime(2026, 8, 6, 9, 0, 0)
            },
            new AuditEntry
            {
                EventType = "import", ProductId = 1,
                Details = "Imported 10,000 — gold_0.5.csv",
                CreatedAt = new DateTime(2026, 8, 9, 11, 0, 0)
            }
        );
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.Add(new PrintJob
        {
            Id = 52, ProductId = 1, PrinterId = 1, Quantity = 500,
            Status = JobStatus.Completed, CodesConfirmed = 500,
            CompletedAt = new DateTime(2026, 8, 10, 14, 30, 0),
            CreatedAt = new DateTime(2026, 8, 10, 14, 0, 0)
        });
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(3);
        // Newest first: Job on Aug 10, Import on Aug 9, Import on Aug 6
        _vm.ActivityHistory[0].Type.Should().Be(ActivityType.JobCompleted);
        _vm.ActivityHistory[1].Type.Should().Be(ActivityType.Import);
        _vm.ActivityHistory[1].Description.Should().Contain("gold_0.5");
        _vm.ActivityHistory[2].Type.Should().Be(ActivityType.Import);
        _vm.ActivityHistory[2].Description.Should().Contain("batch_aug6");
    }

    [Fact]
    public async Task ActivityHistory_MaxTwentyEntries()
    {
        // Requirement: At most 20 entries shown
        for (int i = 0; i < 25; i++)
        {
            _db.AuditLog.Add(new AuditEntry
            {
                EventType = "import", ProductId = 1,
                Details = $"Imported batch {i}",
                CreatedAt = new DateTime(2026, 1, 1 + i, 10, 0, 0)
            });
        }
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCountLessOrEqualTo(20);
    }

    [Fact]
    public async Task ActivityHistory_NotLoadedForFolders()
    {
        // Requirement: History only for leaf products, not folders
        _db.AuditLog.Add(new AuditEntry
        {
            EventType = "import", ProductId = 1,
            Details = "Should not appear",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Folder", IsLeaf = false };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivityHistory_OnlyLoadsForSelectedProductId()
    {
        // Must not show history from other products
        _db.AuditLog.AddRange(
            new AuditEntry
            {
                EventType = "import", ProductId = 1,
                Details = "Product 1 import",
                CreatedAt = DateTime.UtcNow
            },
            new AuditEntry
            {
                EventType = "import", ProductId = 2,
                Details = "Product 2 import - should NOT appear",
                CreatedAt = DateTime.UtcNow
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Product 1", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Description.Should().Contain("Product 1 import");
    }

    [Fact]
    public async Task ActivityHistory_JobError_ShowsRedType()
    {
        // Requirement: Error jobs show with ActivityType.JobError
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.Add(new PrintJob
        {
            Id = 99, ProductId = 1, PrinterId = 1, Quantity = 100,
            Status = JobStatus.Error, CodesConfirmed = 50,
            CompletedAt = new DateTime(2026, 8, 5, 12, 0, 0),
            CreatedAt = new DateTime(2026, 8, 5, 11, 0, 0)
        });
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Type.Should().Be(ActivityType.JobError);
        _vm.ActivityHistory[0].Description.Should().Contain("error");
        _vm.ActivityHistory[0].Description.Should().Contain("50/100");
    }

    [Fact]
    public async Task ActivityHistory_ClearsOnSelectionChange()
    {
        // When switching products, old history is cleared before new loads
        _db.AuditLog.Add(new AuditEntry
        {
            EventType = "import", ProductId = 1,
            Details = "Product 1 import",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(Arg.Any<int>()).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(Arg.Any<int>()).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "P1", IsLeaf = true };
        await Task.Delay(200);
        _vm.ActivityHistory.Should().HaveCount(1);

        // Switch to product with no history
        _vm.SelectedProduct = new ProductNode { Id = 2, Name = "P2", IsLeaf = true };
        await Task.Delay(200);
        _vm.ActivityHistory.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. CODE POOL STATS REFRESH
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodePoolStats_SumsAllStatusesForTotal()
    {
        var stats = new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 1000 },
            { CodeStatus.Reserved, 50 },
            { CodeStatus.Printed, 500 },
            { CodeStatus.Burned, 10 },
            { CodeStatus.Returned, 5 }
        };
        _codePoolService.GetPoolStatsAsync(1).Returns(stats);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(100);

        _vm.TotalCodesCount.Should().Be(1565); // Sum of all statuses
    }

    [Fact]
    public async Task CodePoolStats_ZeroWhenNoCodesExist()
    {
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Empty", IsLeaf = true };
        await Task.Delay(100);

        _vm.AvailableCodesCount.Should().Be(0);
        _vm.PrintedCodesCount.Should().Be(0);
        _vm.BurnedCodesCount.Should().Be(0);
        _vm.TotalCodesCount.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. DELETE GUARDS (Section 3.5)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteGuard_CanDelete_ButtonEnabled()
    {
        _productService.CanDeleteAsync(1).Returns(true);
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Deletable", IsLeaf = true };
        await Task.Delay(100);

        _vm.CanDeleteSelectedProduct.Should().BeTrue();
        _vm.DeleteBlockedReason.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteGuard_CannotDelete_ButtonDisabledWithReason()
    {
        // Requirement: Show explanation when delete is blocked
        _productService.CanDeleteAsync(1).Returns(false);
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Busy", IsLeaf = true };
        await Task.Delay(100);

        _vm.CanDeleteSelectedProduct.Should().BeFalse();
        _vm.DeleteBlockedReason.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeleteGuard_NoSelection_DeleteDisabled()
    {
        _vm.SelectedProduct = null;
        await Task.Delay(100);

        _vm.CanDeleteSelectedProduct.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. NEW JOB NAVIGATION (Section 3.2)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NewJob_LeafSelected_WithAvailableCodes_FiresNavigationEvent()
    {
        // Requirement: Fires NavigateToNewJobRequested with productId, but only if codes available
        _codePoolService.GetPoolStatsAsync(42).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 100 }
        });
        int? firedProductId = null;
        _vm.NavigateToNewJobRequested += (_, id) => firedProductId = id;

        _vm.SelectedProduct = new ProductNode { Id = 42, Name = "Apple", IsLeaf = true };
        await Task.Delay(100);

        _vm.NewJobCommand.Execute(null);

        firedProductId.Should().Be(42);
    }

    [Fact]
    public void NewJob_FolderSelected_DoesNotFire()
    {
        // Requirement: Button disabled for folders
        int? firedProductId = null;
        _vm.NavigateToNewJobRequested += (_, id) => firedProductId = id;

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Folder", IsLeaf = false };
        _vm.NewJobCommand.Execute(null);

        firedProductId.Should().BeNull();
    }

    [Fact]
    public void NewJob_NothingSelected_DoesNotFire()
    {
        int? firedProductId = null;
        _vm.NavigateToNewJobRequested += (_, id) => firedProductId = id;

        _vm.SelectedProduct = null;
        _vm.NewJobCommand.Execute(null);

        firedProductId.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. DESELECT & ROOT-LEVEL CREATION (Section 2.2 / 3.3)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Deselect_ThenAddFolder_CreatesAtRoot()
    {
        // Scenario: User selects a folder, then deselects (clicks empty), then adds
        var folder = new ProductNode { Id = 10, Name = "Juice", IsLeaf = false };
        _vm.SelectedProduct = folder;
        await Task.Delay(50);

        // Deselect
        _vm.SelectedProduct = null;
        await Task.Delay(50);

        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "Root Level Folder";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("Root Level Folder", null);
    }

    [Fact]
    public async Task Deselect_ThenAddProduct_CreatesAtRoot()
    {
        _vm.SelectedProduct = new ProductNode { Id = 5, Name = "Leaf", IsLeaf = true, ParentId = 10 };
        await Task.Delay(50);

        _vm.SelectedProduct = null;
        await Task.Delay(50);

        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "Root Product";
        _vm.NewProductTemplate = "root.rox";
        _vm.NewProductCsvName = "root.csv";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateProductAsync("Root Product", null, "root.rox", "root.csv");
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. EDGE CASES & VALIDATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SelectionChange_RapidSwitching_DoesNotCrash()
    {
        // Rapid selection changes should not produce errors
        _codePoolService.GetPoolStatsAsync(Arg.Any<int>()).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 100 }
        });
        _productService.CanDeleteAsync(Arg.Any<int>()).Returns(true);

        for (int i = 1; i <= 10; i++)
        {
            _vm.SelectedProduct = new ProductNode { Id = i, Name = $"P{i}", IsLeaf = true };
        }

        await Task.Delay(300);

        // Should not throw — last selection wins
        _vm.SelectedProduct!.Id.Should().Be(10);
    }

    [Fact]
    public async Task AddProduct_TemplateAndCsvOptional()
    {
        // Requirement: Only name is required; template and csv can be empty
        _vm.SelectedProduct = null;
        _vm.ShowAddProductCommand.Execute(null);
        _vm.NewNodeName = "MinimalProduct";
        _vm.NewProductTemplate = "";
        _vm.NewProductCsvName = "";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddProductCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateProductAsync("MinimalProduct", null, "", "");
    }

    [Fact]
    public async Task ActivityHistory_HandlesNullDetailsGracefully()
    {
        // Edge case: audit entry with null details
        _db.AuditLog.Add(new AuditEntry
        {
            EventType = "import", ProductId = 1,
            Details = null,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Description.Should().NotBeNull();
    }

    [Fact]
    public async Task ActivityHistory_UsesCompletedAtForJobDate_FallsBackToCreatedAt()
    {
        // Requirement: Date = CompletedAt ?? CreatedAt
        _db.ProductNodes.Add(new ProductNode { Id = 1, Name = "Test", IsLeaf = true });
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.AddRange(
            new PrintJob
            {
                Id = 1, ProductId = 1, PrinterId = 1, Quantity = 10,
                Status = JobStatus.Completed, CodesConfirmed = 10,
                CompletedAt = new DateTime(2026, 8, 10, 14, 0, 0),
                CreatedAt = new DateTime(2026, 8, 10, 10, 0, 0)
            },
            new PrintJob
            {
                Id = 2, ProductId = 1, PrinterId = 1, Quantity = 10,
                Status = JobStatus.Error, CodesConfirmed = 5,
                CompletedAt = null, // no CompletedAt
                CreatedAt = new DateTime(2026, 8, 9, 12, 0, 0)
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        // First item should be the one with CompletedAt (Aug 10 14:00)
        _vm.ActivityHistory[0].Date.Should().Be(new DateTime(2026, 8, 10, 14, 0, 0));
        // Second item uses CreatedAt as fallback (Aug 9 12:00)
        _vm.ActivityHistory[1].Date.Should().Be(new DateTime(2026, 8, 9, 12, 0, 0));
    }

    [Fact]
    public async Task AddFolder_RootLevelLeafSelected_NoParent_CreatesAtRoot()
    {
        // Root-level leaf (ParentId = null) → sibling means root
        var rootLeaf = new ProductNode { Id = 5, Name = "Standalone", IsLeaf = true, ParentId = null };
        _vm.SelectedProduct = rootLeaf;
        await Task.Delay(50);
        _vm.ShowAddFolderCommand.Execute(null);
        _vm.NewNodeName = "SiblingOfRootLeaf";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmAddFolderCommand.ExecuteAsync(null);

        await _productService.Received(1).CreateFolderAsync("SiblingOfRootLeaf", null);
    }

    [Fact]
    public async Task ActivityHistory_NonImportAuditEntries_Excluded()
    {
        // Only 'import' event type should appear, not other audit types
        _db.AuditLog.AddRange(
            new AuditEntry
            {
                EventType = "import", ProductId = 1,
                Details = "Real import",
                CreatedAt = DateTime.UtcNow
            },
            new AuditEntry
            {
                EventType = "delete", ProductId = 1,
                Details = "Product deleted - should not show",
                CreatedAt = DateTime.UtcNow
            },
            new AuditEntry
            {
                EventType = "template_change", ProductId = 1,
                Details = "Template changed - should not show",
                CreatedAt = DateTime.UtcNow
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Description.Should().Contain("Real import");
    }

    [Fact]
    public async Task ActivityHistory_JobFromDifferentProduct_NotShown()
    {
        // Jobs for other products must not appear
        _db.ProductNodes.AddRange(
            new ProductNode { Id = 1, Name = "P1", IsLeaf = true },
            new ProductNode { Id = 2, Name = "P2", IsLeaf = true }
        );
        _db.Printers.Add(new PrinterEntity { Id = 1, Name = "P1", IpAddress = "mock", Port = 9100 });
        _db.PrintJobs.AddRange(
            new PrintJob
            {
                Id = 1, ProductId = 1, PrinterId = 1, Quantity = 10,
                Status = JobStatus.Completed, CodesConfirmed = 10,
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new PrintJob
            {
                Id = 2, ProductId = 2, PrinterId = 1, Quantity = 20,
                Status = JobStatus.Completed, CodesConfirmed = 20,
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            }
        );
        await _db.SaveChangesAsync();

        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(1).Returns(true);

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "P1", IsLeaf = true };
        await Task.Delay(200);

        _vm.ActivityHistory.Should().HaveCount(1);
        _vm.ActivityHistory[0].Description.Should().Contain("10/10");
    }

    [Fact]
    public void ActivityHistoryItem_TypeBrush_CorrectColors()
    {
        // Verify color coding per requirement
        var importItem = new ActivityHistoryItem { Type = ActivityType.Import };
        var completedItem = new ActivityHistoryItem { Type = ActivityType.JobCompleted };
        var cancelledItem = new ActivityHistoryItem { Type = ActivityType.JobCancelled };
        var errorItem = new ActivityHistoryItem { Type = ActivityType.JobError };

        // Blue for import
        importItem.TypeBrush.ToString().Should().Contain("3182CE");
        // Green for completed
        completedItem.TypeBrush.ToString().Should().Contain("38A169");
        // Orange for cancelled
        cancelledItem.TypeBrush.ToString().Should().Contain("DD6B20");
        // Red for error
        errorItem.TypeBrush.ToString().Should().Contain("E53E3E");
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. NEW JOB — DISABLED WHEN AVAILABLE = 0
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CanCreateNewJob_TrueWhenCodesAvailable()
    {
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 500 }
        });

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "WithCodes", IsLeaf = true };
        await Task.Delay(100);

        _vm.CanCreateNewJob.Should().BeTrue();
    }

    [Fact]
    public async Task CanCreateNewJob_FalseWhenZeroAvailable()
    {
        // Requirement: Disable + New Job when available == 0
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Printed, 500 },
            { CodeStatus.Burned, 10 }
        });

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Depleted", IsLeaf = true };
        await Task.Delay(100);

        _vm.CanCreateNewJob.Should().BeFalse();
    }

    [Fact]
    public async Task CanCreateNewJob_FalseWhenEmptyPool()
    {
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Empty", IsLeaf = true };
        await Task.Delay(100);

        _vm.CanCreateNewJob.Should().BeFalse();
    }

    [Fact]
    public async Task CanCreateNewJob_FalseForFolder()
    {
        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Folder", IsLeaf = false };
        await Task.Delay(100);

        _vm.CanCreateNewJob.Should().BeFalse();
    }

    [Fact]
    public async Task CanCreateNewJob_FalseWhenNothingSelected()
    {
        _vm.SelectedProduct = null;
        await Task.Delay(100);

        _vm.CanCreateNewJob.Should().BeFalse();
    }

    [Fact]
    public async Task NewJob_DoesNotFireWhenZeroAvailable()
    {
        // Even though leaf is selected, event must not fire if no codes
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>());
        int? firedProductId = null;
        _vm.NavigateToNewJobRequested += (_, id) => firedProductId = id;

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Empty", IsLeaf = true };
        await Task.Delay(100);

        _vm.NewJobCommand.Execute(null);
        firedProductId.Should().BeNull();
    }

    [Fact]
    public async Task NewJob_FiresWhenCodesAvailable()
    {
        _codePoolService.GetPoolStatsAsync(1).Returns(new Dictionary<CodeStatus, int>
        {
            { CodeStatus.Available, 100 }
        });
        int? firedProductId = null;
        _vm.NavigateToNewJobRequested += (_, id) => firedProductId = id;

        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "HasCodes", IsLeaf = true };
        await Task.Delay(100);

        _vm.NewJobCommand.Execute(null);
        firedProductId.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. RENAME FOLDER & PRODUCT
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ShowRename_SetsEditNameAndFlag()
    {
        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Apple 0.5L", IsLeaf = true };
        _vm.ShowRenameCommand.Execute(null);

        _vm.IsRenaming.Should().BeTrue();
        _vm.EditName.Should().Be("Apple 0.5L");
    }

    [Fact]
    public void ShowRename_NothingSelected_DoesNothing()
    {
        _vm.SelectedProduct = null;
        _vm.ShowRenameCommand.Execute(null);

        _vm.IsRenaming.Should().BeFalse();
    }

    [Fact]
    public void CancelRename_ClearsState()
    {
        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "Test", IsLeaf = true };
        _vm.ShowRenameCommand.Execute(null);

        _vm.CancelRenameCommand.Execute(null);

        _vm.IsRenaming.Should().BeFalse();
        _vm.EditName.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmRename_UpdatesProductName()
    {
        var product = new ProductNode { Id = 5, Name = "Old Name", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "New Name";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        product.Name.Should().Be("New Name");
        await _productService.Received(1).UpdateAsync(product);
        _vm.IsRenaming.Should().BeFalse();
        _vm.EditName.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmRename_FolderWorks()
    {
        // Rename should work for folders too
        var folder = new ProductNode { Id = 3, Name = "Old Folder", IsLeaf = false };
        _vm.SelectedProduct = folder;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "New Folder";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        folder.Name.Should().Be("New Folder");
        await _productService.Received(1).UpdateAsync(folder);
    }

    [Fact]
    public async Task ConfirmRename_TrimsWhitespace()
    {
        var product = new ProductNode { Id = 5, Name = "Old", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "  Trimmed Name  ";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        product.Name.Should().Be("Trimmed Name");
    }

    [Fact]
    public async Task ConfirmRename_EmptyName_DoesNotUpdate()
    {
        var product = new ProductNode { Id = 5, Name = "Original", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "   ";

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        product.Name.Should().Be("Original");
        await _productService.DidNotReceive().UpdateAsync(Arg.Any<ProductNode>());
    }

    [Fact]
    public async Task ConfirmRename_SameName_ClosesWithoutUpdate()
    {
        // No-op if name didn't change
        var product = new ProductNode { Id = 5, Name = "Same", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "Same";

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        _vm.IsRenaming.Should().BeFalse();
        await _productService.DidNotReceive().UpdateAsync(Arg.Any<ProductNode>());
    }

    [Fact]
    public async Task ConfirmRename_RefreshesTree()
    {
        var product = new ProductNode { Id = 5, Name = "Old", IsLeaf = true };
        _vm.SelectedProduct = product;
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.EditName = "Renamed";
        _productService.GetRootsAsync().Returns(new List<ProductNode>());

        await _vm.ConfirmRenameCommand.ExecuteAsync(null);

        // LoadProductsAsync should have been called to refresh the tree
        await _productService.Received().GetRootsAsync();
    }

    [Fact]
    public async Task SelectionChange_ClosesRenameForm()
    {
        // Switching selection should cancel any in-progress rename
        _vm.SelectedProduct = new ProductNode { Id = 1, Name = "First", IsLeaf = true };
        await Task.Delay(50);
        _vm.ShowRenameCommand.Execute(null);
        _vm.IsRenaming.Should().BeTrue();

        _codePoolService.GetPoolStatsAsync(Arg.Any<int>()).Returns(new Dictionary<CodeStatus, int>());
        _productService.CanDeleteAsync(Arg.Any<int>()).Returns(true);

        // Change selection
        _vm.SelectedProduct = new ProductNode { Id = 2, Name = "Second", IsLeaf = true };
        await Task.Delay(50);

        _vm.IsRenaming.Should().BeFalse();
        _vm.EditName.Should().BeEmpty();
    }

    [Fact]
    public void ShowRename_FolderSelected_Works()
    {
        var folder = new ProductNode { Id = 3, Name = "Beverages", IsLeaf = false };
        _vm.SelectedProduct = folder;
        _vm.ShowRenameCommand.Execute(null);

        _vm.IsRenaming.Should().BeTrue();
        _vm.EditName.Should().Be("Beverages");
    }
}
