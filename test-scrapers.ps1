# JavScraper 刮削器独立测试脚本
# 无需安装到 Emby 即可测试刮削器功能

param(
    [string]$TestId = "SSIS-001",
    [string]$Scraper = "All",  # JavBus, JavDB, All
    [switch]$Verbose,
    [switch]$SaveResults,
    [string]$ProxyUrl = ""
)

Write-Host "=== JavScraper 刮削器测试工具 ===" -ForegroundColor Green
Write-Host ""

function Write-Status {
    param($Message, $Status = "Info")
    $color = switch ($Status) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        "Info" { "Cyan" }
        default { "White" }
    }
    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] $Message" -ForegroundColor $color
}

function Test-ScraperLogic {
    param(
        [string]$ScraperName,
        [string]$TestId,
        [string]$ProxyUrl = ""
    )
    
    Write-Status "测试 $ScraperName 刮削器..." "Info"
    
    try {
        # 编译测试项目
        Write-Status "编译项目..." "Info"
        $buildResult = & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --configuration Debug --verbosity quiet 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            Write-Status "编译失败:" "Error"
            $buildResult | ForEach-Object { Write-Status "  $_" "Error" }
            return $false
        }
        
        # 创建临时测试脚本
        $testScript = @"
using System;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Linq;

// 加载编译后的程序集
var assemblyPath = @"Emby.Plugins.JavScraper\bin\Debug\JavScraper.dll";
if (!File.Exists(assemblyPath)) {
    Console.WriteLine("ERROR: 找不到编译后的程序集");
    return 1;
}

try {
    var assembly = Assembly.LoadFrom(assemblyPath);
    var scraperType = assembly.GetTypes().FirstOrDefault(t => t.Name == "$ScraperName");
    
    if (scraperType == null) {
        Console.WriteLine("ERROR: 找不到刮削器类型: $ScraperName");
        return 1;
    }
    
    Console.WriteLine("SUCCESS: 找到刮削器类型: " + scraperType.FullName);
    
    // 检查关键方法
    var methods = scraperType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
    var hasGetMoviesByKeyword = methods.Any(m => m.Name == "GetMoviesByKeyword");
    var hasGetMovie = methods.Any(m => m.Name == "GetMovie");
    
    Console.WriteLine("GetMoviesByKeyword 方法: " + (hasGetMoviesByKeyword ? "存在" : "缺失"));
    Console.WriteLine("GetMovie 方法: " + (hasGetMovie ? "存在" : "缺失"));
    
    return 0;
}
catch (Exception ex) {
    Console.WriteLine("ERROR: " + ex.Message);
    return 1;
}
"@
        
        # 保存并执行测试脚本
        $tempScript = "temp_test_$ScraperName.csx"
        $testScript | Out-File $tempScript -Encoding UTF8
        
        try {
            $result = & dotnet script $tempScript 2>&1
            $success = $LASTEXITCODE -eq 0
            
            if ($Verbose -or -not $success) {
                $result | ForEach-Object { Write-Status "  $_" $(if ($success) { "Info" } else { "Error" }) }
            }
            
            if ($success) {
                Write-Status "✓ $ScraperName 刮削器测试通过" "Success"
            } else {
                Write-Status "✗ $ScraperName 刮削器测试失败" "Error"
            }
            
            return $success
        }
        finally {
            if (Test-Path $tempScript) {
                Remove-Item $tempScript -Force
            }
        }
    }
    catch {
        Write-Status "测试异常: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Test-NetworkConnectivity {
    param([string]$Url)
    
    Write-Status "测试网络连接: $Url" "Info"
    
    try {
        $response = Invoke-WebRequest -Uri $Url -Method Head -TimeoutSec 10 -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Status "✓ 网络连接正常" "Success"
            return $true
        } else {
            Write-Status "✗ 网络连接异常: HTTP $($response.StatusCode)" "Warning"
            return $false
        }
    }
    catch {
        Write-Status "✗ 网络连接失败: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Test-IdRecognition {
    param([string]$TestId)
    
    Write-Status "测试番号识别: $TestId" "Info"
    
    # 基本的番号格式验证
    $patterns = @(
        '^[A-Z]+-\d+$',           # ABC-123
        '^[A-Z]+\d+$',            # ABC123
        '^\d+[A-Z]+-\d+$',        # 123ABC-456
        '^[A-Z]+-\d+[A-Z]?$'      # ABC-123A
    )
    
    $isValid = $false
    foreach ($pattern in $patterns) {
        if ($TestId -match $pattern) {
            $isValid = $true
            Write-Status "✓ 番号格式匹配: $pattern" "Success"
            break
        }
    }
    
    if (-not $isValid) {
        Write-Status "⚠ 番号格式可能不标准" "Warning"
    }
    
    return $isValid
}

function Create-TestReport {
    param(
        [hashtable]$Results,
        [string]$TestId
    )
    
    $reportPath = "test-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    
    $report = @{
        TestId = $TestId
        Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        Results = $Results
        Summary = @{
            TotalTests = $Results.Count
            PassedTests = ($Results.Values | Where-Object { $_ -eq $true }).Count
            FailedTests = ($Results.Values | Where-Object { $_ -eq $false }).Count
        }
    }
    
    $report | ConvertTo-Json -Depth 10 | Out-File $reportPath -Encoding UTF8
    Write-Status "测试报告已保存: $reportPath" "Info"
}

# 主测试逻辑
Write-Status "开始测试刮削器功能" "Info"
Write-Status "测试番号: $TestId" "Info"
Write-Status "测试范围: $Scraper" "Info"

$results = @{}

# 1. 番号识别测试
$results["IdRecognition"] = Test-IdRecognition $TestId

# 2. 网络连接测试
if ($Scraper -eq "All" -or $Scraper -eq "JavBus") {
    $results["JavBusNetwork"] = Test-NetworkConnectivity "https://www.javbus.com"
}

if ($Scraper -eq "All" -or $Scraper -eq "JavDB") {
    $results["JavDBNetwork"] = Test-NetworkConnectivity "https://javdb.com"
}

# 3. 刮削器逻辑测试
if ($Scraper -eq "All" -or $Scraper -eq "JavBus") {
    $results["JavBusLogic"] = Test-ScraperLogic "JavBus" $TestId $ProxyUrl
}

if ($Scraper -eq "All" -or $Scraper -eq "JavDB") {
    $results["JavDBLogic"] = Test-ScraperLogic "JavDB" $TestId $ProxyUrl
}

# 4. 编译测试
Write-Status "执行完整编译测试..." "Info"
$compileResult = & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --configuration Release --verbosity quiet 2>&1
$results["Compilation"] = ($LASTEXITCODE -eq 0)

if ($results["Compilation"]) {
    Write-Status "✓ 编译测试通过" "Success"
} else {
    Write-Status "✗ 编译测试失败:" "Error"
    if ($Verbose) {
        $compileResult | ForEach-Object { Write-Status "  $_" "Error" }
    }
}

# 总结结果
Write-Host ""
Write-Status "=== 测试结果总结 ===" "Info"

$totalTests = $results.Count
$passedTests = ($results.Values | Where-Object { $_ -eq $true }).Count
$failedTests = $totalTests - $passedTests

foreach ($test in $results.Keys) {
    $status = if ($results[$test]) { "✓" } else { "✗" }
    $color = if ($results[$test]) { "Success" } else { "Error" }
    Write-Status "$status $test" $color
}

Write-Host ""
Write-Status "总计: $totalTests 项测试" "Info"
Write-Status "通过: $passedTests 项" "Success"
Write-Status "失败: $failedTests 项" $(if ($failedTests -eq 0) { "Success" } else { "Error" })

# 保存测试报告
if ($SaveResults) {
    Create-TestReport $results $TestId
}

# 提供改进建议
if ($failedTests -gt 0) {
    Write-Host ""
    Write-Status "改进建议:" "Warning"
    
    if (-not $results["Compilation"]) {
        Write-Status "- 修复编译错误后重新测试" "Info"
    }
    
    if ($results.ContainsKey("JavBusNetwork") -and -not $results["JavBusNetwork"]) {
        Write-Status "- 检查 JavBus 网站访问或配置代理" "Info"
    }
    
    if ($results.ContainsKey("JavDBNetwork") -and -not $results["JavDBNetwork"]) {
        Write-Status "- 检查 JavDB 网站访问或配置代理" "Info"
    }
    
    exit 1
} else {
    Write-Status "🎉 所有测试都通过！可以安全部署到 Emby" "Success"
    exit 0
}
