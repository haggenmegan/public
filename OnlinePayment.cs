using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Data.Entity.Validation;
using System.Web.Mvc;
using System.IO;
using System.Net.Mail;
using System.Configuration;
using System.Threading.Tasks;

namespace webPortals.Models
{
    public class OnlinePayment : PersistantEntity
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Matter Id")]
        public int MatterId { get; set; }

        [Required]
        public string PaymentType { get; set; }

        [Required]
        [Display(Name = "Transaction Id")]
        public string TransactionId { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Convenience Fee (3.5% + $0.30)")]
        public decimal Fee { get; set; }

        public double ConvenienceFeePercent { get; set; }
        public double ChargeServiceFeePercent { get; set; }
        public decimal ConvenienceFeeDollars { get; set; }
        public decimal ChargeServiceFeeDollars { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Payment")]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        [Required]
        public bool IsSuccessful { get; set; }

        [Required]
        public string LastChargeServiceStatus { get; set; }

        public bool IsNotificationSent { get; set; }

        public string SettlementBatchId { get; set; }

        [Required]
        [Display(Name = "Transaction Date")]
        [DisplayFormat(DataFormatString = "{0:MM/dd/yyyy}")]
        public DateTime TransactionDate { get; set; }

        public OnlinePayment() 
        {
            ConvenienceFeePercent = (.035);
            ChargeServiceFeePercent = (.029);
            ConvenienceFeeDollars = (decimal)(.30);
            ChargeServiceFeeDollars = (decimal)(.30);
        }

        public bool Create()
        {
            Errors = new ModelStateDictionary();
            bool isSuccessful = false;
            using (var dbContext = new ApplicationDbContext())
            {
                try
                {
                    IsNotificationSent = false;
                    dbContext.Payments.Add(this);
                    dbContext.SaveChanges();
                    isSuccessful = true;
                    SaveFile(this);
                }
                catch (DbEntityValidationException e)
                {
                    e.Improve();
                    foreach (var error in e.Data.Values)
                    {
                        Errors.AddModelError("Payment", error.ToString());
                    }
                    isSuccessful = false;
                }
                catch(Exception e)
                {
                    Errors.AddModelError("Payment", e.ToString());
                    isSuccessful = false;
                    //for testing
                    //throw;
                }
            }
            return isSuccessful;
        }

        private void SaveFile(OnlinePayment payment)
        {
            string path;
            var communityId = new Matter.Matter(payment.MatterId).Find().CommunityAssociationId;
            var ca = new Matter.CommunityAssociation { CommunityAssociationId = communityId };
            var clientNumber = ca.Find().ClientNumber;

            path = string.Format(ConfigurationManager.AppSettings["DocumentArchive"] + "{0}\\{1}\\Acctng {2}\\OnlinePayment_{3}.txt", clientNumber, payment.MatterId, payment.MatterId, payment.TransactionId);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (StreamWriter writer =
                new StreamWriter(path))
                {
                    writer.WriteLine("Payment Id: " + payment.TransactionId);
                    writer.WriteLine("Matter Id: " + payment.MatterId);
                    writer.WriteLine("Payment: " + payment.Amount);
                    writer.WriteLine("Convenience Fee: " + payment.Fee);
                    writer.WriteLine("Total Amount: " + payment.TotalAmount);
                    writer.WriteLine("Payment Type: " + payment.PaymentType);
                    writer.WriteLine("Date: " + DateTime.Now.ToString("MM-dd-yyyy"));
                }
            }
            catch(Exception e)
            {
                throw;
            }
        }

        public async Task EmailDetailsAsync(string to, string subject)
        {
            var appsettings = ConfigurationManager.AppSettings;
            MailMessage email = new MailMessage(new MailAddress(appsettings["Smtp.DefaultFromAddress"], appsettings["Smtp.DefaultFromUser"]), new MailAddress(to));
            email.CC.Add(new MailAddress(appsettings["EmailTo"]));
            email.Subject = subject;
            email.IsBodyHtml = true;
            string message = "<html><body><table>";

            //iterate over payment properties and add name and value to email message
            foreach (var prop in this.GetType().GetProperties())
            {
                message += "<tr><td>" + prop.Name + "</td><td>" + prop.GetValue(this, null) + "</td></tr>";
            }
            message += "</table></body></html>";
            email.Body = message;

            using (var mailClient = new SmtpService())
            {
                await mailClient.SendMailAsync(email);
            }
        }

        public static List<OnlinePayment> GetList(int matterId)
        {
            using (var dbContext = new ApplicationDbContext())
            {
                return dbContext.Payments.Where(x => x.MatterId == matterId).ToList();
            }
        }

        public static OnlinePayment Find(int id)
        {
            using (var dbContext = new ApplicationDbContext())
            {
                return dbContext.Payments.Where(x => x.Id == id).FirstOrDefault();
            }
        }
    }
}