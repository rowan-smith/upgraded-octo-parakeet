using ForgeDeck.Contracts.Services;

namespace ForgeDeck.Review.Application;

public sealed class ReviewDomainService : IReviewService
{
    public string ServiceId => "review";
}
