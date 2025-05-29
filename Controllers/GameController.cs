using BondRun.Data;
using BondRun.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BondRun.Controllers;

[ApiController]
[Route("api/game")]
public class GameController(ApiDbContext db) : ControllerBase
{
    [HttpGet("get-history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery]int page = 0,
        [FromQuery]int pageSize = 10)
    {
        var total = await db.Games.CountAsync();
        
        var items = await db.Games
            .OrderByDescending(g => g.PlayedAt)
            .AsNoTracking()
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(g => g.WinningSide)
            .ToListAsync();

        return Ok(new
        {
            Items = items,
            HasMore = (page + 1) * pageSize < total
        });
    }
}