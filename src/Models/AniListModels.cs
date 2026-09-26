using System.Collections.Generic;

namespace AniListWatchTime.Models
{
    public class GraphQlResponse<T>
    {
        public T Data { get; set; }
        public List<GraphQlError> Errors { get; set; }
    }

    public class GraphQlError
    {
        public string Message { get; set; }
    }

    public class ViewerResponse
    {
        public ViewerPayload User { get; set; }
    }

    public class ViewerPayload
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MediaListCollectionResponse
    {
        public MediaListCollectionPayload List { get; set; }
    }

    public class MediaListCollectionPayload
    {
        public List<MediaListGroup> Lists { get; set; }
    }

    public class MediaListGroup
    {
        public List<MediaListEntry> Entries { get; set; }
    }

    public class MediaListEntry
    {
        public int? Progress { get; set; }
        public int? UpdatedAt { get; set; }
        public MediaSummary Media { get; set; }
    }

    public class MediaSummary
    {
        public long Id { get; set; }
        public int? Duration { get; set; }
        public string Format { get; set; }
    }

    public class MediaEntryResponse
    {
        public MediaEntryPayload Media { get; set; }
    }

    public class MediaEntryPayload
    {
        public int? Duration { get; set; }
        public MediaListSubEntry MediaListEntry { get; set; }
    }

    public class MediaListSubEntry
    {
        public int? Progress { get; set; }
        public int? UpdatedAt { get; set; }
    }

    public class ImporterSettingsConfig
    {
        public string AccountAccessCode { get; set; }
    }

    public class JwtPayload
    {
        public string Sub { get; set; }
    }
}