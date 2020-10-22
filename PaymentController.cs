using System;
using System.Collections.Generic;
using System.Web.Mvc;
using webPortals.Models;
using webPortals.Models.Matter;
using Stripe;
using System.Threading.Tasks;
using System.Configuration;

namespace webPortals.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        [HttpGet]
        public ActionResult New()
        {
            Models.Stripe model = new Models.Stripe();

            ModelState.Merge((ModelStateDictionary)TempData["ModelState"]);
            Matter matter = new Matter(User.GetMatterId()).Find();
            var assessment = matter.GetAssessment();
            model.PaymentEmail = User.GetEmailAddress();

            ViewBag.CompanyName = ConfigurationManager.AppSettings["CompanyName"];

            ViewBag.Assessment = assessment == null ? "" : string.Format("Be sure to include your {0} Assessment of ${1:0.00} when making your payment.", assessment.Item1, assessment.Item2);
            ViewBag.Disclaimer = "NOTICE OF LEGAL RIGHTS TO HOMEOWNER:  A homeowners association (‘HOA”) may not foreclose on your home until the amount of your delinquent assessments, NOT including any late charges, fees and costs of collection, attorney’s fees, or interest, is more than $1,800.00 or your assessments are more than twelve (12) months delinquent.  You have the right to make payments of any amount on your outstanding debt, regardless of whether you are in a payment plan.  The HOA must apply your payments to your actual assessment debt before they are applied to any late charges, fees and costs of collection, attorney’s fees, or interest.  If your delinquent assessments are below $1,800.00 or are less than twelve (12) months delinquent, the HOA may not foreclose but may still file a lawsuit in court to recover any delinquent assessments and additional amounts that are lawfully due.";
            ViewBag.ConvenienceFee = string.Format("Convenience Fee: 3.5% + $0.30 per transaction.  To avoid paying a convenience fee, please mail a check to {0}, {1}", ConfigurationManager.AppSettings["CompanyName"], ConfigurationManager.AppSettings["CompanyAddress"]);
            return View(model);
        }

        #region stripe

        [HttpPost]
        public async Task<ActionResult> CreateAsync(Models.Stripe model)
        {
            var total = model.amount + model.Fee;
            if (total != model.TotalAmount)
            {
                ModelState.AddModelError("TotalAmount", "Transaction Failed.  Payment and Fee do not add up to Total Amount.  Please try again.");
                TempData["ModelState"] = ModelState;
                return RedirectToAction("New");
            }
            try
            {
                model.TotalAmountCents = (int)Math.Truncate(model.TotalAmount * 100M);
            }
            catch (FormatException e)
            {
                ModelState.AddModelError("TotalAmount", "Error: 81503: Amount is an invalid format.");
                TempData["ModelState"] = ModelState;
                return RedirectToAction("New");
            }

            if(string.IsNullOrEmpty(model.Token))
            {
                ModelState.AddModelError("", "There was a problem with the Checkout Token.");
                TempData["ModelState"] = ModelState;
                return RedirectToAction("New");
            }
            if(string.IsNullOrEmpty(model.PaymentEmail))
            {
                ModelState.AddModelError("", "You must provide an email address with your payment.");
                TempData["ModelState"] = ModelState;
                return RedirectToAction("New");
            }

            model.MatterId = User.GetMatterId();
            model.PaymentDescription = model.MatterId.ToString() + " online payment from " + model.PaymentEmail;
            
            var result = model.MakePayment();
            //success here just means the transaction was successfully sent to Stripe.  It does not reflect Stripe's response
            if (result.Item1)
            {
                var matterId = model.MatterId;
                var stripe = new Models.Stripe();
                var transactionTuple = stripe.GetTransaction(result.Item2);
                if (transactionTuple.Item1)
                {
                    Models.OnlinePayment payment = new Models.OnlinePayment
                    {
                        PaymentType = transactionTuple.Item2.Source.Card.Brand,
                        TransactionId = transactionTuple.Item2.Id.ToString(),
                        Fee = model.Fee,
                        Amount = model.amount,
                        TotalAmount = model.TotalAmount,
                        IsSuccessful = transactionTuple.Item2.Status.ToString().ToLower() == "succeeded",
                        LastChargeServiceStatus = transactionTuple.Item2.Status.ToString(),
                        TransactionDate = transactionTuple.Item2.Created
                    };

                    if (matterId < 0)
                    {
                        //send an email to customer support with the payment details that were not saved.
                        await payment.EmailDetailsAsync(ConfigurationManager.AppSettings["EmailTo"], "An online payment was transmitted but there was a problem finding the associated Matter Id");
                    }
                    else
                    {
                        payment.MatterId = matterId;
                        try
                        {
                            //send an email to customer support with the payment details that were not saved.
                            if (!payment.Create())
                            {
                                await payment.EmailDetailsAsync(ConfigurationManager.AppSettings["EmailTo"], "An online payment was transmitted but not saved to the database");
                            }
                        }
                        catch(Exception e)
                        {
                            ViewBag.errorMessage = e.Message + " " + e.InnerException;
                            return View("Error");
                        }
                    }
                }
               
                TempData["Transaction"] = transactionTuple.Item2;
                TempData["Amount"] = model.amount;
                TempData["Fee"] = model.Fee;
                //for debugging, set to true
                TempData["ShowDetails"] = false;
                return RedirectToAction("ShowTransaction", new { isSuccessful = transactionTuple.Item1 });
            }
            ModelState.AddModelError("TotalAmount", result.Item2);
            TempData["ModelState"] = ModelState;
            return RedirectToAction("New");
        }

        #endregion stripe

        public ActionResult ShowTransaction(bool? isSuccessful)
        {
            if (TempData["Transaction"] == null || isSuccessful == null)
            {
                return RedirectToAction("Index", "Home");
            }
            var transaction = (StripeCharge)TempData["Transaction"];
            if ((bool)isSuccessful)
            {
                TempData["header"] = "Payment Submitted";
                TempData["icon"] = "success";
                TempData["message"] = "Your transaction has been successfully processed.";
            }
            else
            {
                TempData["header"] = "Transaction Failed";
                TempData["icon"] = "fail";
                TempData["message"] = "Your transaction failed with an error of " + transaction.Status + ".  Please contact customer service.";
            }
            if ((TempData["ShowDetails"] != null && (bool)TempData["ShowDetails"]) || (bool)isSuccessful)
            {
                ViewBag.Transaction = transaction;
                ViewBag.AmountInDollars = (transaction.Amount / 100M);
                ViewBag.Amount = TempData["Amount"];
                ViewBag.Fee = TempData["Fee"];
            }
            return View();
        }

        [HttpGet]
        public ActionResult List()
        {
            var matterId = User.GetMatterId();
            List<Models.OnlinePayment> model = Models.OnlinePayment.GetList(matterId);
            return View(model);
        }

        [HttpPost]
        public ActionResult List(string id)
        {
            TempData["id"] = id;
            return RedirectToAction("Show");
        }

        [HttpGet]
        public ActionResult Show()
        {
            if (TempData["id"] == null)
            {
                return RedirectToAction("Index", "Home");
            }
            var payment = Models.OnlinePayment.Find(Int32.Parse(TempData["id"].ToString()));
            return View(payment);
        }
    }
}