using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AtemDirector.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AtemDirector.Server.Pages.Admin
{
    [Authorize]
    [IgnoreAntiforgeryToken]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public IndexModel(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public IList<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
        public IList<Category> Categories { get; set; } = new List<Category>();
        
        // RoomID -> List of Users
        public Dictionary<string, List<Program.VoiceClient>> ActiveRooms { get; set; } = new Dictionary<string, List<Program.VoiceClient>>();
        
        [BindProperty(SupportsGet = true)]
        public int? SelectedCategoryId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string View { get; set; } = "Admin";

        [BindProperty]
        public string Type { get; set; } = "Image";

        [BindProperty]
        public string Description { get; set; } = "";

        [BindProperty]
        public string? YoutubeUrl { get; set; }

        [BindProperty]
        public IFormFile? UploadFile { get; set; }
        
        [BindProperty]
        public int? CategoryId { get; set; }

        public async Task OnGetAsync()
        {
            // Load categories
            Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
            
            // Filter media by selected category
            var query = _db.MediaItems.Include(m => m.Category).AsQueryable();
            if (SelectedCategoryId.HasValue)
            {
                query = query.Where(m => m.CategoryId == SelectedCategoryId.Value);
            }
            MediaItems = await query.OrderByDescending(m => m.CreatedAt).ToListAsync();

            // Fetch active rooms snapshot
            foreach (var roomKv in Program.VoiceRooms)
            {
                var roomId = roomKv.Key;
                var users = new List<Program.VoiceClient>();
                
                foreach (var wsKv in roomKv.Value)
                {
                    if (Program.VoiceClientInfo.TryGetValue(wsKv.Value, out var client))
                    {
                        users.Add(client);
                    }
                }
                ActiveRooms[roomId] = users;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Description))
            {
                return RedirectToPage();
            }

            var newItem = new MediaItem
            {
                Type = Type,
                Description = Description,
                CreatedAt = DateTime.Now,
                CategoryId = CategoryId // Can be null for "General"
            };

            if (Type == "Youtube")
            {
                newItem.Url = YoutubeUrl ?? "";
            }
            else // Image or Video
            {
                if (UploadFile != null && UploadFile.Length > 0)
                {
                    // Ensure uploads folder exists
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploadsFolder);

                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(UploadFile.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await UploadFile.CopyToAsync(stream);
                    }

                    newItem.Url = "/uploads/" + fileName;
                }
            }

            _db.MediaItems.Add(newItem);
            await _db.SaveChangesAsync();

            return RedirectToPage();
        }
        
        [TempData]
        public string? Message { get; set; }
        
        public async Task<IActionResult> OnPostSendMediaAsync(int mediaId, string targetAlias)
        {
            Console.WriteLine($"[Admin] OnPostSendMediaAsync called. MediaId={mediaId}, Target='{targetAlias}'");

            // Value comes as "RoomId|UserAlias"
            if (string.IsNullOrEmpty(targetAlias) || !targetAlias.Contains("|")) 
            {
                Message = "Error: Invalid target selected.";
                return RedirectToPage();
            }
            
            var parts = targetAlias.Split('|');
            var room = parts[0];
            var alias = parts[1];

            var item = await _db.MediaItems.FindAsync(mediaId);
            if (item != null)
            {
                bool success = await Program.SendMediaToUserAsync(room, alias, item.Url, item.Type, item.Description ?? "");
                if (success) 
                    Message = $"Successfully sent '{item.Description}' to {alias}.";
                else
                    Message = $"Failed to send to {alias}. User might be disconnected.";
            }
            else
            {
                Message = "Error: Media item not found.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var item = await _db.MediaItems.FindAsync(id);
            if (item != null)
            {
                // Optional: Delete physical file if it exists locally
                if (item.Type != "Youtube" && item.Url.StartsWith("/uploads/"))
                {
                    try
                    {
                        var localPath = Path.Combine(_env.WebRootPath, item.Url.TrimStart('/'));
                        if (System.IO.File.Exists(localPath))
                        {
                            System.IO.File.Delete(localPath);
                        }
                    }
                    catch { /* ignore cleanup errors */ }
                }

                _db.MediaItems.Remove(item);
                await _db.SaveChangesAsync();
            }
            return RedirectToPage();
        }
        
        public async Task<IActionResult> OnPostCreateCategoryAsync(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                Message = "Category name cannot be empty.";
                return RedirectToPage();
            }
            
            // Check for duplicate
            if (await _db.Categories.AnyAsync(c => c.Name == categoryName))
            {
                Message = "Category already exists.";
                return RedirectToPage();
            }
            
            var category = new Category
            {
                Name = categoryName,
                CreatedAt = DateTime.Now
            };
            
            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
            Message = $"Category '{categoryName}' created successfully.";
            
            return RedirectToPage();
        }
        
        public async Task<IActionResult> OnPostDeleteCategoryAsync(int categoryId)
        {
            var category = await _db.Categories.FindAsync(categoryId);
            if (category != null)
            {
                _db.Categories.Remove(category);
                await _db.SaveChangesAsync();
                Message = $"Category '{category.Name}' deleted.";
            }
            return RedirectToPage();
        }
    }
}
