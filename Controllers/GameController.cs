﻿using BondRun.Data;
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
        [FromQuery]int pageSize = 30)
    {
        var items = await db.Games
            .OrderByDescending(g => g.PlayedAt)
            .AsNoTracking()
            .Take(pageSize)
            .Select(g => g.WinningSide)
            .ToListAsync();

        return Ok(new { Items = items });
    }
}