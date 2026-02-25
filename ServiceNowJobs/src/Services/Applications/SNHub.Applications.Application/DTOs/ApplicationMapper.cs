using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Domain.Entities;

namespace SNHub.Applications.Application.DTOs;

public static class ApplicationMapper
{
    public static ApplicationDto ToDto(JobApplication a) => new(
        Id:               a.Id,
        JobId:            a.JobId,
        CandidateId:      a.CandidateId,
        Status:           a.Status.ToString(),
        CoverLetter:      a.CoverLetter,
        CvUrl:            a.CvUrl,
        EmployerNotes:    a.EmployerNotes,
        RejectionReason:  a.RejectionReason,
        AppliedAt:        a.AppliedAt,
        UpdatedAt:        a.UpdatedAt,
        StatusChangedAt:  a.StatusChangedAt);
}
