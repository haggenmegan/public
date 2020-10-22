using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Data.Entity;
using System.Collections.Generic;

namespace webPortals.Models.Matter
{
    public enum MatterStatus { InPaymentPlanJudgement = 100120, InPaymentPlanSettlement = 100131, InPaymentPlan = 100003, InPaymentPlanEscrow = 100067 }

    [Table("Matter")]
    public class Matter
    {
        [Key]
        [Display(Name = "Matter Id")]
        public int MatterId { get; set; }

        public bool IsActive { get; private set; }
        public int CommunityAssociationId { get; set; }
        public DateTime CreateDt { get; set; }
        public DateTime? CloseDate { get; set; }
        public decimal AssessmentAmountOverride { get; set; }
        public int StatusId { get; set; }
        public DateTime? LedgerVerifiedDate { get; set; }
        public bool IsOwnerWebAccessible { get; set; }
        public int AddressId { get; set; }

        public List<Charge> ChargeList { get; set; }
        public List<Activity> ActivityList { get; set; }
        public static List<int> PaymentPlanStatuses { get; set; }

        public Hoa Hoa { get; set; }

        public ManagementCompany ManagementCompany { get; set; }

        public Matter() { }

        public Matter(int matterId)
        {
            MatterId = matterId;
        }

        private static void SetPaymentPlanStatuses()
        {
            PaymentPlanStatuses = new List<int>();
            PaymentPlanStatuses.Add((int)MatterStatus.InPaymentPlan);
            PaymentPlanStatuses.Add((int)MatterStatus.InPaymentPlanEscrow);
            PaymentPlanStatuses.Add((int)MatterStatus.InPaymentPlanJudgement);
            PaymentPlanStatuses.Add((int)MatterStatus.InPaymentPlanSettlement);
        }

        public Matter Find()
        {
            using (var ProLiveContext = new ProLiveDbContext())
            {
                return ProLiveContext.Matter.Where(x => x.MatterId == MatterId).FirstOrDefault();
            }
        }

        public Tuple<bool,string> IsValid(long registrationId)
        {
            using(var ProLiveContext = new ProLiveDbContext())
            {
                Matter record = ProLiveContext.Matter.FirstOrDefault(x => x.MatterId == MatterId);
                if(record == null)
                {
                    return new Tuple<bool, string>(false, "This Matter Id is not recognized.  Please verify you've typed in the correct Id and contact Customer Support if the problem continues.");
                }
                else if(!record.IsOwnerWebAccessible || record.CloseDate != null || !record.IsActive)
                {
                    return new Tuple<bool, string>(false, "This Matter Id is not eligible for access.  Please contact Customer Support.");
                }
                var sourceRegistrationId = record.CreateDt.ToString("yyyyMMddhhmm");
                if (registrationId.ToString() != sourceRegistrationId.ToString())
                {
                    return new Tuple<bool, string>(false, "Matter Id / Registration Id mismatch.  Please verify you have typed the correct numbers");
                }
                return new Tuple<bool, string>(true, null);
            }
        }

        public bool IsAvailable()
        {
            using(var ApplicationContext = new ApplicationDbContext())
            {
                return !ApplicationContext.Users.Any(x => x.MatterId == MatterId);
            }
        }

        public bool IsLoginAllowed()
        {
            using (var ProLiveContext = new ProLiveDbContext())
            {
                Matter record = ProLiveContext.Matter.FirstOrDefault(x => x.MatterId == MatterId && x.IsActive && x.CloseDate == null && x.IsOwnerWebAccessible);
                return (record != null);
            }
        }

        public static bool IsAccountReviewed(int matterId)
        {
            if (matterId < 0) return false;
            using (var ProLiveContext = new ProLiveDbContext())
            {
                var sixMonthsAgo = DateTime.Today.AddMonths(-6);
                var matter = ProLiveContext.Matter.Where(m => m.MatterId == matterId && m.LedgerVerifiedDate != null).FirstOrDefault();
                if (matter != null && matter.LedgerVerifiedDate >= sixMonthsAgo)
                {
                    return true;
                }
                return false;
            }
        }

        public Tuple<string,decimal,DateTime> GetAssessment()
        {
            using (var ProLiveContext = new ProLiveDbContext())
            {
                var record = ProLiveContext.CommunityAssociationAssessmentSchedule.Include(c => c.CommunityAssociation).Include(s => s.AssessmentSchedule).Where(x => x.CommunityAssociationId == CommunityAssociationId && x.EndDate == null).FirstOrDefault();
                if (record == null)
                {
                    return null;
                }
                decimal regularAssessmentAmount = 0;
                if (record.CommunityAssociation.HasVariableRates != null && (bool)record.CommunityAssociation.HasVariableRates)
                {
                    regularAssessmentAmount = AssessmentAmountOverride;
                }
                else
                {
                    regularAssessmentAmount = record.AssessmentAmount;
                }
                var now = DateTime.Now;
                DateTime dueDate;
                var paymentPlan = ProLiveContext.PaymentPlan.Where(x => x.MatterId == MatterId && x.IsActive).FirstOrDefault();
                if(PaymentPlanStatuses == null)
                {
                    SetPaymentPlanStatuses();
                }
                if (PaymentPlanStatuses.Contains(StatusId) && paymentPlan != null && paymentPlan.NextDueDate >= now)
                {
                    dueDate = paymentPlan.NextDueDate;
                }
                else
                {
                    var day = record.DayOfAssessment;
                    dueDate = new DateTime(now.Year, now.Month, day).AddMonths(1);
                }

                var schedule = record.AssessmentSchedule.AssessmentScheduleName;

                return new Tuple<string, decimal,DateTime>(schedule, regularAssessmentAmount, dueDate);
            }
        }

        public static List<Activity> GetActivities(int matterId, int activityTypeId)
        {
            using (var ProLiveContext = new ProLiveDbContext())
            {
                return ProLiveContext.Activity.Where(a => a.MatterId == matterId && a.ActivityTypeId == activityTypeId && a.IsActive).ToList();
            }
        }
    }
}