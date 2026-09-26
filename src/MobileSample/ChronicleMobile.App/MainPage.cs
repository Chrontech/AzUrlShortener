using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using ChronicleMobile.Core;

namespace ChronicleMobile.App;

public sealed class MainPage : ContentPage
{
    // Premium Dark Theme Palette
    private static readonly Color BgColor = Color.FromArgb("#0F172A"); // Slate 900
    private static readonly Color CardBgColor = Color.FromArgb("#1E293B"); // Slate 800
    private static readonly Color CardBorderColor = Color.FromArgb("#334155"); // Slate 700
    private static readonly Color TextPrimary = Color.FromArgb("#F8FAFC"); // Slate 50
    private static readonly Color TextSecondary = Color.FromArgb("#94A3B8"); // Slate 400
    private static readonly Color TextMuted = Color.FromArgb("#64748B"); // Slate 500
    
    private static readonly Color AccentColor = Color.FromArgb("#6366F1"); // Indigo 500
    private static readonly Color SuccessColor = Color.FromArgb("#10B981"); // Emerald 500
    private static readonly Color WarningColor = Color.FromArgb("#F59E0B"); // Amber 500
    private static readonly Color ErrorColor = Color.FromArgb("#EF4444"); // Red 500

    // Core layout views
    private readonly Label statusBadgeLabel;
    private readonly Border statusBadgeBorder;
    private readonly Label statusHeadline;
    private readonly Label statusDescription;
    private readonly Border shortIdBadge;
    private readonly Label shortIdText;

    private readonly ActivityIndicator loadingIndicator;
    private readonly Label loadingLabel;
    private readonly VerticalStackLayout loadingContainer;

    private readonly Border metadataSectionCard;
    private readonly Label metadataTitle;
    private readonly VerticalStackLayout metadataList;

    public MainPage(ActivationCoordinator activation)
    {
        // Page setup
        Title = "Chronicle";
        BackgroundColor = BgColor;
        On<iOS>().SetUseSafeArea(true);

        // 1. App Header
        var headerBrandLabel = new Label
        {
            Text = "CHRONICLE",
            FontAttributes = FontAttributes.Bold,
            FontSize = 26,
            TextColor = TextPrimary,
            CharacterSpacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10, 0, 2)
        };

        var headerSubtitleLabel = new Label
        {
            Text = "Mobile Link Resolver",
            FontSize = 13,
            TextColor = TextSecondary,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var headerDivider = new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = Color.FromArgb("#1E293B"),
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, 0, 0, 24)
        };

        // 2. Status Badge Pill
        statusBadgeLabel = new Label
        {
            Text = "READY",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = AccentColor,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        statusBadgeBorder = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Stroke = AccentColor,
            StrokeThickness = 1.5,
            BackgroundColor = Color.FromArgb("#1A1F38"), // Subtle indigo tint background
            Padding = new Thickness(12, 4),
            HorizontalOptions = LayoutOptions.Center,
            Content = statusBadgeLabel
        };

        // 3. Status Information
        statusHeadline = new Label
        {
            Text = "Awaiting Link",
            FontAttributes = FontAttributes.Bold,
            FontSize = 20,
            TextColor = TextPrimary,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 8)
        };

        statusDescription = new Label
        {
            Text = "Open a Chronicle mobile link on this device to display its metadata.",
            FontSize = 14,
            TextColor = TextSecondary,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(12, 0, 12, 16)
        };

        // 4. Short ID Pill/Badge
        shortIdText = new Label
        {
            Text = string.Empty,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = TextPrimary,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        shortIdBadge = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Stroke = CardBorderColor,
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#0B1329"), // Deep dark badge background
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 8),
            IsVisible = false,
            Content = shortIdText
        };

        // 5. Card to house status info
        var statusCard = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = CardBorderColor,
            StrokeThickness = 1.5,
            BackgroundColor = CardBgColor,
            Padding = new Thickness(20, 24),
            Margin = new Thickness(0, 0, 0, 20),
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { statusBadgeBorder, statusHeadline, shortIdBadge, statusDescription }
            }
        };

        // 6. Loading Container
        loadingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            Color = AccentColor,
            HorizontalOptions = LayoutOptions.Center,
            HeightRequest = 40,
            WidthRequest = 40
        };

        loadingLabel = new Label
        {
            Text = "Resolving Chronicle Link...",
            FontSize = 14,
            TextColor = TextSecondary,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };

        loadingContainer = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(0, 30),
            IsVisible = false,
            Children = { loadingIndicator, loadingLabel }
        };

        // 7. Metadata Details Card
        metadataTitle = new Label
        {
            Text = "METADATA (UNTRUSTED)",
            FontAttributes = FontAttributes.Bold,
            FontSize = 11,
            TextColor = TextMuted,
            CharacterSpacing = 1.5,
            Margin = new Thickness(4, 0, 0, 12)
        };

        metadataList = new VerticalStackLayout
        {
            Spacing = 12
        };

        metadataSectionCard = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = CardBorderColor,
            StrokeThickness = 1.5,
            BackgroundColor = CardBgColor,
            Padding = new Thickness(16, 20),
            Margin = new Thickness(0, 0, 0, 24),
            IsVisible = false,
            Content = new VerticalStackLayout
            {
                Children = { metadataTitle, metadataList }
            }
        };

        // 8. Bottom Security/Privacy Notice
        var footerShield = new Label
        {
            Text = "Display Only",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = TextMuted,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10, 0, 2)
        };

        var footerNotice = new Label
        {
            Text = "This sample treats all resolved metadata as untrusted. No actions or automatic navigation will be triggered.",
            FontSize = 11,
            TextColor = TextMuted,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(24, 0, 24, 10)
        };

        var footerContainer = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { footerShield, footerNotice }
        };

        // 9. Root Layout and ScrollView
        Content = new Microsoft.Maui.Controls.ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 24, 20, 24),
                Spacing = 0,
                Children =
                {
                    headerBrandLabel,
                    headerSubtitleLabel,
                    headerDivider,
                    loadingContainer,
                    statusCard,
                    metadataSectionCard,
                    footerContainer
                }
            }
        };

        // Wire up notifications
        activation.PropertyChanged += (_, _) =>
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                Render(activation);
            });
        };

        // Initial render
        Render(activation);
    }

    private void Render(ActivationCoordinator activation)
    {
        var result = activation.Result;
        var isLoading = activation.IsLoading;

        // Toggle Loading State
        if (isLoading)
        {
            loadingContainer.IsVisible = true;
            loadingIndicator.IsRunning = true;
            
            // Subtly dim or hide status elements during loading
            statusBadgeBorder.IsVisible = false;
            statusHeadline.IsVisible = false;
            statusDescription.IsVisible = false;
            shortIdBadge.IsVisible = false;
            metadataSectionCard.IsVisible = false;
            return;
        }

        // Restore visibility of elements
        loadingContainer.IsVisible = false;
        loadingIndicator.IsRunning = false;
        
        statusBadgeBorder.IsVisible = true;
        statusHeadline.IsVisible = true;
        statusDescription.IsVisible = true;

        // Render based on Status
        switch (result.Status)
        {
            case ResolutionStatus.Normal:
                statusBadgeLabel.Text = "READY";
                statusBadgeLabel.TextColor = AccentColor;
                statusBadgeBorder.Stroke = AccentColor;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#1E1B4B"); // Deep Indigo Background
                
                statusHeadline.Text = "Awaiting Link";
                statusDescription.Text = "Open a Chronicle mobile link on this device to display its metadata.";
                
                shortIdBadge.IsVisible = false;
                metadataSectionCard.IsVisible = false;
                break;

            case ResolutionStatus.InvalidUri:
                statusBadgeLabel.Text = "INVALID";
                statusBadgeLabel.TextColor = WarningColor;
                statusBadgeBorder.Stroke = WarningColor;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#451A03"); // Deep Amber Background
                
                statusHeadline.Text = "Invalid Deep Link";
                statusDescription.Text = "The activation link is missing a valid or correctly encoded 'shortid' query parameter.";
                
                shortIdBadge.IsVisible = false;
                metadataSectionCard.IsVisible = false;
                break;

            case ResolutionStatus.Resolved:
                statusBadgeLabel.Text = "RESOLVED";
                statusBadgeLabel.TextColor = SuccessColor;
                statusBadgeBorder.Stroke = SuccessColor;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#064E3B"); // Deep Emerald Background
                
                statusHeadline.Text = "Link Resolved";
                statusDescription.Text = "The resolver returned the associated metadata. Review the details below.";
                
                // Set Short ID badge
                shortIdText.Text = $"ID: {result.ShortId}";
                shortIdBadge.IsVisible = true;

                // Render Metadata
                metadataSectionCard.IsVisible = true;
                metadataList.Children.Clear();

                if (result.Metadata is null || result.Metadata.Count == 0)
                {
                    // Empty Dictionary state
                    var emptyLabel = new Label
                    {
                        Text = "Empty metadata dictionary received ({}).",
                        FontSize = 13,
                        TextColor = TextSecondary,
                        FontAttributes = FontAttributes.Italic,
                        HorizontalOptions = LayoutOptions.Fill,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 10)
                    };
                    metadataList.Children.Add(emptyLabel);
                }
                else
                {
                    var count = 0;
                    foreach (var pair in result.Metadata)
                    {
                        count++;
                        var rowLayout = new VerticalStackLayout
                        {
                            Spacing = 4,
                            Padding = new Thickness(0, 2)
                        };

                        var keyLabel = new Label
                        {
                            Text = pair.Key.ToUpperInvariant(),
                            FontSize = 11,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = TextSecondary,
                            CharacterSpacing = 1
                        };

                        var valLabel = new Label
                        {
                            Text = pair.Value,
                            FontSize = 14,
                            TextColor = TextPrimary,
                            LineBreakMode = LineBreakMode.WordWrap
                        };

                        rowLayout.Children.Add(keyLabel);
                        rowLayout.Children.Add(valLabel);

                        // If not the last item, add a thin divider line
                        if (count < result.Metadata.Count)
                        {
                            var divider = new BoxView
                            {
                                HeightRequest = 1,
                                BackgroundColor = Color.FromArgb("#334155"),
                                Margin = new Thickness(0, 8, 0, 4),
                                HorizontalOptions = LayoutOptions.Fill
                            };
                            rowLayout.Children.Add(divider);
                        }

                        metadataList.Children.Add(rowLayout);
                    }
                }
                break;

            case ResolutionStatus.Missing:
                statusBadgeLabel.Text = "NOT FOUND";
                statusBadgeLabel.TextColor = ErrorColor;
                statusBadgeBorder.Stroke = ErrorColor;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#7F1D1D"); // Deep Red Background
                
                statusHeadline.Text = "Short Link Missing (404)";
                statusDescription.Text = "The specified mobile link was not found.";
                
                shortIdText.Text = $"ID: {result.ShortId}";
                shortIdBadge.IsVisible = true;
                metadataSectionCard.IsVisible = false;
                break;

            case ResolutionStatus.Archived:
                statusBadgeLabel.Text = "ARCHIVED";
                statusBadgeLabel.TextColor = TextSecondary;
                statusBadgeBorder.Stroke = TextSecondary;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#1E293B"); // Slate Gray Background
                
                statusHeadline.Text = "Link Archived (410)";
                statusDescription.Text = "This mobile link has been archived and is no longer active.";
                
                shortIdText.Text = $"ID: {result.ShortId}";
                shortIdBadge.IsVisible = true;
                metadataSectionCard.IsVisible = false;
                break;

            case ResolutionStatus.Error:
            default:
                statusBadgeLabel.Text = "ERROR";
                statusBadgeLabel.TextColor = ErrorColor;
                statusBadgeBorder.Stroke = ErrorColor;
                statusBadgeBorder.BackgroundColor = Color.FromArgb("#7F1D1D"); // Deep Red Background
                
                statusHeadline.Text = "Resolution Failed";
                statusDescription.Text = "Unable to resolve the link due to a network offline state, server timeout, or invalid JSON response.";
                
                if (!string.IsNullOrEmpty(result.ShortId))
                {
                    shortIdText.Text = $"ID: {result.ShortId}";
                    shortIdBadge.IsVisible = true;
                }
                else
                {
                    shortIdBadge.IsVisible = false;
                }
                metadataSectionCard.IsVisible = false;
                break;
        }
    }
}
