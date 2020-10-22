using System;
using System.Configuration;
using System.ComponentModel.DataAnnotations;
using Stripe;
using System.ComponentModel.DataAnnotations.Schema;

namespace webPortals.Models
{
    [NotMapped]
    public class StripeSettings : ConfigurationSection
    {

        [ConfigurationProperty("publicKey")]
        public string PublicKey
        {
            get { return (string)this["publicKey"]; }
        }

        [ConfigurationProperty("privateKey")]
        public string PrivateKey
        {
            get { return (string)this["privateKey"]; }
        }

        [ConfigurationProperty("environment")]
        public string Environment
        {
            get { return (string)this["environment"]; }
        }

        [ConfigurationProperty("endpoint")]
        public string Endpoint
        {
            get { return (string)this["endpoint"]; }
        }

        [ConfigurationProperty("requireZipCode")]
        public bool RequireZipCode
        {
            get { return (bool)this["requireZipCode"]; }
        }

        [ConfigurationProperty("allowRememberMe")]
        public bool AllowRememberMe
        {
            get { return (bool)this["allowRememberMe"]; }
        }

        [ConfigurationProperty("statementDescriptor")]
        public string StatementDescriptor
        {
            get { return (string)this["statementDescriptor"]; }
        }
    }

    public class Stripe
    {
        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Payment")]
        public decimal amount { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Convenience Fee")]
        public decimal Fee { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        [Range(.01, Double.MaxValue)]
        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        public int TotalAmountCents { get; set; }
        public string PaymentDescription { get; set; }

        [Required]
        public string Token { get; set; }
        public string Environment { get; set; }
        public string PublicKey { get; set; }
        private string PrivateKey { get; set; }
        public bool RequireZipCode { get; set; }
        public bool AllowRememberMe { get; set; }
        [Required]
        public string PaymentEmail { get; set; }
        private string StatementDescriptor { get; set; }
        public int MatterId { get; set; }

        private static StripeSettings settings = (StripeSettings)ConfigurationManager.GetSection("StripeSettings");

        private static StripeSettings GetSettings { get { return settings; } }

        public Stripe()
        {
            settings = GetSettings;
            Environment = settings.Environment;
            PublicKey = settings.PublicKey;
            PrivateKey = settings.PrivateKey;
            RequireZipCode = settings.RequireZipCode;
            AllowRememberMe = settings.AllowRememberMe;
            StatementDescriptor = settings.StatementDescriptor;
        }

        public Tuple<bool,string> MakePayment()
        {
            try
            {
                StripeConfiguration.SetApiKey(PrivateKey);

                var customers = new StripeCustomerService();
                var charges = new StripeChargeService();

                var customer = customers.Create(new StripeCustomerCreateOptions
                {
                    Email = PaymentEmail,
                    Description = MatterId.ToString(),
                    SourceToken = Token
                });

                if (customer.Sources.Data.Count > 0)
                {
                    var country = customer.Sources.Data[0].Card.Country;
                    if(country != "US")
                    {
                        Fee = Math.Round((.01M * amount) + (.035M * amount) + .3M, 2);
                        TotalAmountCents = (int)Math.Truncate((Fee + amount) * 100M);
                        TotalAmount = TotalAmountCents / 100M;
                    }

                    var chargeOptions = new StripeChargeCreateOptions()
                    {
                        Amount = TotalAmountCents,
                        Currency = "usd",
                        Description = PaymentDescription,
                        StatementDescriptor = StatementDescriptor,
                        CustomerId = customer.Id,
                        Capture = true //auth & settle (capture) in one step
                    };
                    var chargeService = new StripeChargeService();
                    StripeCharge charge = chargeService.Create(chargeOptions);
                    return new Tuple<bool, string>(true, charge.Id);
                }
                return new Tuple<bool, string>(false, "There was a problem processing your credit card.");
            }
            catch (StripeException e)
            {
                return new Tuple<bool, string>(false, e.StripeError.ErrorType + ": " + e.StripeError.Code + " " + e.StripeError.Message);
            }
        }

        public Tuple<bool,StripeCharge> GetTransaction(string id)
        {
            StripeCharge response = new StripeCharge();
            bool isSuccessful = true;

            try
            {
                StripeConfiguration.SetApiKey(PrivateKey);
                var charges = new StripeChargeService();
                var result = charges.Get(id);
                return new Tuple<bool, StripeCharge>(isSuccessful,charges.Get(id));
            }
            catch (StripeException e)
            {
                response.FailureMessage = e.StripeError.ErrorType + ": " + e.StripeError.Code + " " + e.StripeError.Message;
                response.Id = id;
                isSuccessful = false;
            }
            return new Tuple<bool, StripeCharge>(isSuccessful,response);
        }
    }

}