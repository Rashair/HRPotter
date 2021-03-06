﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HRPotter.Data;
using HRPotter.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static HRPotter.Controllers.UsersController;

namespace HRPotter.Controllers
{
    [Route("[controller]")]
    [Authorize]
    public class JobApplicationsController : Controller
    {
        const int maxFileSize = (int)5e6;
        const string separationString = "#_#";

        private readonly HRPotterContext _context;
        private readonly BlobContainerClient blobContainerClient;

        public JobApplicationsController(HRPotterContext context, IHttpContextAccessor httpContextAccessor,
            BlobServiceClient blobService)
        {
            _context = context;
            blobContainerClient = blobService.GetBlobContainerClient("job-applications");
            if (!IsAuthorized())
            {
                AuthorizeUser(_context, httpContextAccessor.HttpContext.User);
            }
        }

        /// <summary>
        /// Main page
        /// </summary>
        /// <returns> Job applications list</returns>
        [Route("[action]")]
        [Route("")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            List<JobApplication> result;

            if (HRPotterUser.Role == "Admin")
            {
                result = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).ToListAsync();
            }
            else
            {
                result = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).Where(u => u.CreatorId == HRPotterUser.Id).ToListAsync();
            }

            return View(result);
        }

        [Route("[action]")]
        [HttpGet]
        public async Task<IActionResult> GetApplicationsTable([FromQuery(Name = "query")] string searchString)
        {
            List<JobApplication> applications;
            if (string.IsNullOrEmpty(searchString))
            {
                if (HRPotterUser.Role == "Admin")
                {
                    applications = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).ToListAsync();
                }
                else
                {
                    applications = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).
                        Where(u => u.CreatorId == HRPotterUser.Id).ToListAsync();
                }
            }
            else
            {
                if (HRPotterUser.Role == "Admin")
                {
                    applications = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).
                        Where(app => app.JobOffer.JobTitle.Contains(searchString)).
                        ToListAsync();
                }
                else
                {
                    applications = await _context.JobApplications.Include(x => x.JobOffer).ThenInclude(y => y.Company).
                        Where(u => u.CreatorId == HRPotterUser.Id).
                        Where(app => app.JobOffer.JobTitle.Contains(searchString)).
                        ToListAsync();
                }
            }

            return PartialView("_ApplicationsTable", applications);
        }

        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Route("[action]/{id}")]
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            var app = await _context.JobApplications.Include(x => x.JobOffer).FirstOrDefaultAsync(o => o.Id == id);
            if (app == null)
            {
                return NotFound($"Application with corresponding id was not found: {id}");
            }

            if (app.CreatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            return View(app);
        }

        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]/{id}")]
        [HttpGet]
        public async Task<IActionResult> DetailsHR(int? id)
        {
            if (HRPotterUser.Role != "HR")
            {
                return Forbid();
            }

            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            var jobApplication = await _context.JobApplications.Include(x => x.JobOffer).FirstOrDefaultAsync(app => app.Id == id);
            if (jobApplication == null)
            {
                return NotFound($"Application with corresponding id was not found: {id}");
            }

            return View(jobApplication);
        }


        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int? id, int? status)
        {
            if (HRPotterUser.Role != "HR")
            {
                return Forbid();
            }

            if (id == null || status == null || !Enum.IsDefined(typeof(ApplicationStatus), status.Value))
            {
                return BadRequest();
            }

            JobApplication app = await _context.JobApplications.Include(x => x.JobOffer).FirstOrDefaultAsync(app => app.Id == id);
            if (app == null)
            {
                return NotFound();
            }

            if (app.JobOffer.CreatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            app.Status = (ApplicationStatus)status.Value;
            _context.Update(app);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(DetailsHR), "JobOffers", new { id = app.JobOfferId });
        }


        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]")]
        [HttpGet]
        public async Task<IActionResult> Create(int? offerId)
        {
            if (offerId == null)
            {
                return BadRequest("Id cannot be null");
            }

            JobOffer offer = await _context.JobOffers.FirstOrDefaultAsync(jobOffer => jobOffer.Id == offerId.Value);
            if (offer == null)
            {
                return NotFound($"Offer with corresponding id was not found: {offerId}");
            }

            JobApplication jobApplication = new JobApplication
            {
                JobOfferId = offerId.Value,
                JobOffer = offer
            };

            return View(jobApplication);
        }

        [Route("[action]")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(JobApplication model, [FromForm] IFormFile cvFile)
        {
            if (HRPotterUser.Role != "User")
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string cvUrl = null;
            if (cvFile != null && cvFile.Length < maxFileSize && cvFile.Length > 0)
            {
                try
                {
                    cvUrl = await UploadFile(cvFile);
                }
                catch (FileLoadException)
                {
                    return RedirectToAction("Error", "Home");
                }
            }
            else if (cvFile != null)
            {
                return RedirectToAction("Error", "Home");
            }

            JobApplication app = new JobApplication
            {
                CreatorId = HRPotterUser.Id,
                JobOfferId = model.JobOfferId,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                Phone = model.Phone,
                CvUrl = cvUrl,
                IsStudent = model.IsStudent,
                Description = model.Description,
                Status = ApplicationStatus.ToBeReviewed
            };

            await _context.JobApplications.AddAsync(app);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<string> UploadFile(IFormFile file)
        {
            var filename = Guid.NewGuid().ToString() + separationString + file.FileName; ;
            var blobClient = blobContainerClient.GetBlobClient(filename);
            using var uploadStream = file.OpenReadStream();
            try
            {
                await blobClient.UploadAsync(uploadStream);
            }
            catch
            {
                throw new FileLoadException();
            }

            return filename;
        }

        [Route("[action]")]
        [ActionName("Download")]
        [HttpGet]
        public async Task<IActionResult> DownloadBlobFile(string name)
        {
            var blobClient = blobContainerClient.GetBlobClient(name);
            BlobDownloadInfo download;
            try
            {
                download = await blobClient.DownloadAsync();
            }
            catch
            {
                return RedirectToAction("Error", "Home");
            }

            var ind = name.IndexOf(separationString);
            string downloadFileName = name.Substring(ind > 0 ? ind + separationString.Length : 0);
            return File(download.Content, download.ContentType, downloadFileName);
        }


        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]/{id}")]
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            var jobApplication = await _context.JobApplications.Include(x => x.JobOffer).
                FirstOrDefaultAsync(app => app.Id == id);

            if (jobApplication == null)
            {
                return NotFound();
            }

            if (jobApplication.CreatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            if (jobApplication.Status != ApplicationStatus.ToBeReviewed)
            {
                return BadRequest();
            }

            return View(jobApplication);
        }

        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]/{id}")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(JobApplication model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            JobApplication app = await _context.JobApplications.FirstOrDefaultAsync(app => app.Id == model.Id);
            if (app == null)
            {
                return BadRequest();
            }
            if (app.CreatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            app.FirstName = model.FirstName;
            app.LastName = model.LastName;
            app.Email = model.Email;
            app.Phone = model.Phone;
            app.CvUrl = model.CvUrl;
            app.IsStudent = model.IsStudent;
            app.Description = model.Description;
            _context.Update(app);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", new { id = model.Id });
        }

        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]/{id}")]
        [HttpPost]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            int? creatorId = _context.JobApplications.Where(app => app.Id == id).Select(app => app.CreatorId).FirstOrDefault();
            if (creatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            if (!creatorId.HasValue)
            {
                return RedirectToAction("Index");
            }

            _context.Remove(new JobApplication { Id = id.Value });
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Route("[action]/{id}")]
        [HttpGet]
        public async Task<IActionResult> ApplicationsForOffer(int? id, [FromQuery(Name = "query")] string searchString)
        {
            if (id == null)
            {
                return BadRequest("Id cannot be null");
            }

            int? creatorId = _context.JobApplications.Where(app => app.Id == id).Select(app => app.CreatorId).FirstOrDefault();
            if (creatorId != HRPotterUser.Id)
            {
                return Forbid();
            }

            if (!creatorId.HasValue)
            {
                PartialView("_ApplicationsTable", new List<JobApplication>());
            }

            List<JobApplication> result;
            if (String.IsNullOrEmpty(searchString))
            {
                result = await _context.JobApplications.Where(app => app.JobOfferId == id).ToListAsync();
            }
            else
            {
                result = await _context.JobApplications.Where(app => app.JobOfferId == id &&
                   (app.FirstName.Contains(searchString) || app.LastName.Contains(searchString))).
                   ToListAsync();
            }

            return PartialView("_ApplicationsTable", result);
        }
    }
}
