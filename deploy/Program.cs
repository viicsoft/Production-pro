using System;
using System.IO;
using System.Linq;
using Renci.SshNet;

const string host = "173.249.37.10";
const string user = "root";
const string pass = "viicsoft";
const string remoteAppDir = "/var/www/atemdirector-server";
const string localPublishDir = @"C:\Production-pro-main\Production-pro-main\server\publish";

Console.WriteLine($"[Deploy] Connecting to {host}...");

using var ssh = new SshClient(host, user, pass);
ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
ssh.Connect();
Console.WriteLine("[Deploy] SSH connected.");

using var sftp = new SftpClient(host, user, pass);
sftp.Connect();
Console.WriteLine("[Deploy] SFTP connected.");

string Run(string label, string cmd)
{
    Console.WriteLine($"  [{label}]");
    var result = ssh.RunCommand(cmd);
    if (!string.IsNullOrWhiteSpace(result.Result)) Console.WriteLine($"    {result.Result.Trim()}");
    if (!string.IsNullOrEmpty(result.Error)) Console.WriteLine($"    [stderr] {result.Error.Trim()}");
    return result.Result;
}

// ==== STEP 1: Stop the service ====
Console.WriteLine("\n=== Step 1: Stopping atemdirector service ===");
Run("stop", "systemctl stop atemdirector");
Run("verify stopped", "systemctl is-active atemdirector || true");

// ==== STEP 2: Upload new publish files ====
Console.WriteLine("\n=== Step 2: Uploading new files ===");

// Upload all DLLs and config files from publish dir (NOT wwwroot — we handle that separately)
var publishFiles = Directory.GetFiles(localPublishDir, "*", SearchOption.TopDirectoryOnly);
int uploaded = 0;
foreach (var file in publishFiles)
{
    var filename = Path.GetFileName(file);
    var remotePath = $"{remoteAppDir}/{filename}";
    
    using var fileStream = File.OpenRead(file);
    sftp.UploadFile(fileStream, remotePath, true);
    uploaded++;
    Console.Write($"\r  Uploaded {uploaded}/{publishFiles.Length}: {filename}                ");
}
Console.WriteLine();

// ==== STEP 3: Upload wwwroot files ====
Console.WriteLine("\n=== Step 3: Uploading wwwroot files ===");

var wwwrootLocal = Path.Combine(localPublishDir, "wwwroot");
if (Directory.Exists(wwwrootLocal))
{
    // Ensure remote wwwroot exists
    Run("mkdir wwwroot", $"mkdir -p {remoteAppDir}/wwwroot");
    
    var wwwFiles = Directory.GetFiles(wwwrootLocal, "*", SearchOption.TopDirectoryOnly);
    int wwwUploaded = 0;
    foreach (var file in wwwFiles)
    {
        var filename = Path.GetFileName(file);
        var remotePath = $"{remoteAppDir}/wwwroot/{filename}";
        
        using var fileStream = File.OpenRead(file);
        sftp.UploadFile(fileStream, remotePath, true);
        wwwUploaded++;
        Console.Write($"\r  Uploaded {wwwUploaded}/{wwwFiles.Length}: {filename}                ");
    }
    Console.WriteLine();
    
    // Upload uploads subdirectory contents if any
    var uploadsLocal = Path.Combine(wwwrootLocal, "uploads");
    if (Directory.Exists(uploadsLocal))
    {
        Run("mkdir uploads", $"mkdir -p {remoteAppDir}/wwwroot/uploads");
    }
}

// ==== STEP 4: Remove stale pre-compressed files ====
Console.WriteLine("\n=== Step 4: Removing stale pre-compressed files ===");
Run("rm .br/.gz", $"rm -f {remoteAppDir}/wwwroot/*.br {remoteAppDir}/wwwroot/*.gz");

// ==== STEP 5: Fix Nginx WebSocket config ====
Console.WriteLine("\n=== Step 5: Fixing Nginx WebSocket config ===");
// The current config has 'Connection keep-alive' which breaks WebSocket upgrades
// Need to change it to 'Connection "upgrade"' for WebSocket to work
var nginxCheck = Run("check nginx config", "grep 'Connection keep-alive' /etc/nginx/sites-available/vidikom.app && echo 'NEEDS_FIX' || echo 'OK'");

if (nginxCheck.Contains("NEEDS_FIX"))
{
    Console.WriteLine("  Fixing nginx WebSocket proxy config...");
    Run("fix connection header", @"sed -i 's/proxy_set_header Connection keep-alive;/proxy_set_header Connection $http_connection;/' /etc/nginx/sites-available/vidikom.app");
    
    // Also add WebSocket timeout settings
    Run("add ws timeout", @"sed -i '/proxy_cache_bypass/a\        proxy_read_timeout 86400s;\n        proxy_send_timeout 86400s;' /etc/nginx/sites-available/vidikom.app");
    
    Run("test nginx", "nginx -t");
    Run("reload nginx", "systemctl reload nginx");
}
else
{
    Console.WriteLine("  Nginx config looks OK.");
}

// ==== STEP 6: Start the service ====
Console.WriteLine("\n=== Step 6: Starting atemdirector service ===");
Run("start", "systemctl start atemdirector");
System.Threading.Thread.Sleep(2000); // Give it time to start
Run("status", "systemctl is-active atemdirector");

// ==== STEP 7: Verify ====
Console.WriteLine("\n=== Step 7: Verification ===");
Run("port check", "ss -tlnp | grep 5160");
Run("curl test", "curl -s -o /dev/null -w '%{http_code}' http://localhost:5160/ || echo 'FAIL'");
Run("curl intercom token", "curl -s 'http://localhost:5160/api/intercom/token?identity=test&name=test' | head -c 200 || echo 'FAIL'");

Console.WriteLine("\n=== Step 8: Final nginx config ===");
Run("final nginx", "cat /etc/nginx/sites-available/vidikom.app");

sftp.Disconnect();
ssh.Disconnect();
Console.WriteLine("\n✅ Deployment complete!");
