using System;
using System.IO;
using System.Text;
using Renci.SshNet;

const string host = "161.97.121.251";
const string user = "root";
const string pass = "viicsoft";
const string remoteAppDir = "/var/www/atemdirector-server";
const string localTarFile = @"C:\Production-pro-main\Production-pro-main\server\publish_linux.tar.gz";

Console.WriteLine("========================================");
Console.WriteLine($"[Deploy] Connecting to {host} as {user}...");
Console.WriteLine("========================================");

using var ssh = new SshClient(host, user, pass);
ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
ssh.Connect();
Console.WriteLine("[Deploy] SSH connected.");

using var sftp = new SftpClient(host, user, pass);
sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
sftp.Connect();
Console.WriteLine("[Deploy] SFTP connected.");

if (args.Length > 0 && args[0] == "quick")
{
    Console.WriteLine("Quick uploading director-intercom.html...");
    using (var fs = File.OpenRead(@"C:\Production-pro-main\Production-pro-main\server\wwwroot\director-intercom.html"))
    {
        sftp.UploadFile(fs, "/var/www/atemdirector-server/wwwroot/director-intercom.html", true);
    }
    Console.WriteLine("Uploaded director-intercom.html successfully!");
    sftp.Disconnect();
    ssh.Disconnect();
    return;
}

if (args.Length > 1 && args[0] == "cmd")
{
    var cmd = string.Join(" ", args.Skip(1));
    Console.WriteLine($"Running remote command: {cmd}");
    var res = ssh.RunCommand(cmd);
    Console.WriteLine(res.Result);
    if (!string.IsNullOrEmpty(res.Error)) Console.Error.WriteLine(res.Error);
    sftp.Disconnect();
    ssh.Disconnect();
    return;
}

string Run(string label, string cmd)
{
    Console.WriteLine($"\n>> [{label}]: {cmd}");
    var result = ssh.RunCommand(cmd);
    if (!string.IsNullOrWhiteSpace(result.Result))
    {
        foreach (var line in result.Result.Trim().Split('\n'))
        {
            Console.WriteLine($"   {line.TrimEnd()}");
        }
    }
    if (!string.IsNullOrEmpty(result.Error))
    {
        foreach (var line in result.Error.Trim().Split('\n'))
        {
            Console.WriteLine($"   [stderr] {line.TrimEnd()}");
        }
    }
    return result.Result;
}

// ==== STEP 1: Stop existing service ====
Console.WriteLine("\n=== Step 1: Stopping existing atemdirector service ===");
Run("stop service", "systemctl stop atemdirector || true");

// ==== STEP 2: Prepare remote directory & upload archive ====
Console.WriteLine("\n=== Step 2: Preparing directory and uploading release tarball ===");
Run("mkdir app dir", $"mkdir -p {remoteAppDir}");

Console.WriteLine($"  Uploading {localTarFile} via SFTP...");
using (var fileStream = File.OpenRead(localTarFile))
{
    var remoteTar = $"{remoteAppDir}/publish_linux.tar.gz";
    sftp.UploadFile(fileStream, remoteTar, true);
}
Console.WriteLine("  Upload completed successfully.");

// ==== STEP 3: Extract archive and set permissions ====
Console.WriteLine("\n=== Step 3: Extracting files and setting permissions ===");
Run("extract tarball", $"cd {remoteAppDir} && tar -xzf publish_linux.tar.gz && rm -f publish_linux.tar.gz");
Run("set permissions", $"chmod +x {remoteAppDir}/server && chmod -R 755 {remoteAppDir}");

// ==== STEP 4: Configure systemd service ====
Console.WriteLine("\n=== Step 4: Configuring systemd service ===");
var serviceContent = @"[Unit]
Description=AtemDirector Server and Cloud Relay
After=network.target

[Service]
WorkingDirectory=/var/www/atemdirector-server
ExecStart=/var/www/atemdirector-server/server
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=atemdirector
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5160

[Install]
WantedBy=multi-user.target
";

var remoteServicePath = "/etc/systemd/system/atemdirector.service";
using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(serviceContent)))
{
    sftp.UploadFile(ms, remoteServicePath, true);
}
Console.WriteLine("  Uploaded atemdirector.service");

Run("daemon-reload", "systemctl daemon-reload");
Run("enable service", "systemctl enable atemdirector");
Run("start service", "systemctl restart atemdirector");
System.Threading.Thread.Sleep(2500);
Run("check service status", "systemctl is-active atemdirector");
Run("check port 5160", "ss -tlnp | grep 5160 || true");

// ==== STEP 5: Configure Nginx ====
Console.WriteLine("\n=== Step 5: Configuring Nginx reverse proxy ===");
var nginxConfig = @"server {
    listen 80;
    listen [::]:80;
    server_name vidikom.app www.vidikom.app;
    client_max_body_size 250M;

    location / {
        proxy_pass http://127.0.0.1:5160;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection ""upgrade"";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }
}
";

var remoteNginxAvailable = "/etc/nginx/sites-available/vidikom.app";
using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(nginxConfig)))
{
    sftp.UploadFile(ms, remoteNginxAvailable, true);
}
Console.WriteLine("  Uploaded /etc/nginx/sites-available/vidikom.app");

Run("symlink nginx site", "ln -sf /etc/nginx/sites-available/vidikom.app /etc/nginx/sites-enabled/vidikom.app");
Run("test nginx config", "nginx -t");
Run("reload nginx", "systemctl reload nginx");

// ==== STEP 6: SSL with Certbot ====
Console.WriteLine("\n=== Step 6: Obtaining / Renewing SSL with Certbot ===");
Run("certbot SSL", "certbot --nginx -d vidikom.app -d www.vidikom.app --non-interactive --agree-tos -m admin@vidikom.app --redirect || true");

// Ensure WebSocket proxy headers remain in place after Certbot modifications
Run("ensure ws upgrade header in nginx", @"sed -i 's/proxy_set_header Connection keep-alive;/proxy_set_header Connection ""upgrade"";/' /etc/nginx/sites-available/vidikom.app || true");
Run("reload nginx post-ssl", "nginx -t && systemctl reload nginx");

// ==== STEP 7: Verify endpoints ====
Console.WriteLine("\n=== Step 7: Verifying deployment endpoints ===");
Run("local port check", "curl -s -o /dev/null -w 'HTTP %{http_code}\n' http://127.0.0.1:5160/crew");
Run("local token check", "curl -s 'http://127.0.0.1:5160/api/intercom/token?identity=director&name=Director'");
Run("external https check", "curl -s -k -o /dev/null -w 'HTTPS %{http_code}\n' https://vidikom.app/crew");
Run("external token check", "curl -s -k 'https://vidikom.app/api/intercom/token?identity=director&name=Director'");

sftp.Disconnect();
ssh.Disconnect();

Console.WriteLine("\n========================================");
Console.WriteLine("✅ AtemDirector deployed to vidikom.app successfully!");
Console.WriteLine("========================================");
