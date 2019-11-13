﻿using HRPotter.Data;
using HRPotter.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HRPotter.Controllers
{
    public class JobOffersController : Controller
    {
        private readonly HRPotterContext _context;

        public JobOffersController(HRPotterContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery(Name = "search")] string searchString)
        {
            if (string.IsNullOrEmpty(searchString))
            {
                List<JobOffer> offers = await _context.JobOffers.Include(x => x.Company).ToListAsync();
                return View(offers);
            }

            List<JobOffer> searchResult = await _context.JobOffers.Include(x => x.Company).
                Where(offer => offer.JobTitle.Contains(searchString, StringComparison.InvariantCultureIgnoreCase)).
                ToListAsync();
            return View(searchResult);
        }

        [HttpGet]
        public async Task<IActionResult> IndexHR([FromQuery(Name = "search")] string searchString)
        {
            if (string.IsNullOrEmpty(searchString))
            {
                List<JobOffer> offers = await _context.JobOffers.Include(x => x.Company).ToListAsync();
                return View(offers);
            }

            List<JobOffer> searchResult = await _context.JobOffers.Include(x => x.Company).
                Where(offer => offer.JobTitle.Contains(searchString, StringComparison.InvariantCultureIgnoreCase)).
                ToListAsync();
            return View(searchResult);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            JobOffer offer = await _context.JobOffers.Include(x => x.Company).FirstOrDefaultAsync(o => o.Id == id);
            if (offer == null)
            {
                return NotFound($"Offer with corresponding id was not found: {id}");
            }

            return View(offer);
        }

        public async Task<IActionResult> DetailsHR(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            JobOffer offer = await _context.JobOffers.
                Include(x => x.Company).
                Include(x => x.JobApplications).
                FirstOrDefaultAsync(o => o.Id == id);
            if (offer == null)
            {
                return NotFound($"Offer with corresponding id was not found: {id}");
            }

            return View(offer);
        }

        public async Task<ActionResult> Create()
        {
            var model = new JobOfferCreateView
            {
                Companies = await _context.Companies.ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(JobOfferCreateView model)
        {
            if (!ModelState.IsValid)
            {
                model.Companies = await _context.Companies.ToListAsync();
                return View(model);
            }

            JobOffer offer = new JobOffer
            {
                CompanyId = model.CompanyId,
                Description = model.Description,
                JobTitle = model.JobTitle,
                Location = model.Location,
                SalaryFrom = model.SalaryFrom,
                SalaryTo = model.SalaryTo,
                ValidUntil = model.ValidUntil,
                Created = DateTime.Now,
            };

            await _context.JobOffers.AddAsync(offer);
            await _context.SaveChangesAsync();

            return RedirectToAction("IndexHR");
        }


        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            JobOffer offer = await _context.JobOffers.FirstOrDefaultAsync(o => o.Id == id);
            if (offer == null)
            {
                return NotFound($"Offer with corresponding id was not found: {id}");
            }

            var createView = new JobOfferCreateView(offer)
            {
                Companies = await _context.Companies.ToListAsync()
            };

            return View(createView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(JobOffer model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            JobOffer offer = await _context.JobOffers.FirstOrDefaultAsync(o => o.Id == model.Id);

            offer.JobTitle = model.JobTitle;
            offer.Description = model.Description;
            offer.CompanyId = model.CompanyId;
            offer.Location = model.Location;
            offer.SalaryFrom = model.SalaryFrom;
            offer.SalaryTo = model.SalaryTo;
            offer.ValidUntil = model.ValidUntil;
            _context.Update(offer);
            await _context.SaveChangesAsync();

            return RedirectToAction("DetailsHR", new { id = model.Id });
        }

        [HttpPost]
        public async Task<ActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            _context.Remove(new JobOffer { Id = id.Value });
            await _context.SaveChangesAsync();

            return RedirectToAction("IndexHR");
        }
    }
}