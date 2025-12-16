# Token-Based Login Feature Implementation

## Overview
This feature allows passing a login token through the TSB URL handler that gets automatically appended to the start URL configured in the .seb file.

## How It Works

### 1. URL Handler Enhancement
The TSB URL handler now supports extracting a `token` parameter from the URL:
```
tsb://s3.amazonaws.com/bucket/config.seb?token=abc123xyz
```

### 2. Token Extraction
- The `ConfigurationOperation` extracts the token from the command line URL
- Token is stored in `SessionConfiguration.LoginToken`
- Token is copied to `BrowserSettings.LoginToken` when settings are loaded

### 3. Start URL Injection
- The `BrowserApplication.GenerateStartUrl()` method automatically appends the token
- Token is properly URL-encoded and added as a query parameter
- Works with existing query parameters in the start URL

## Example Usage

### Magic Link URL
```
tsb://s3.amazonaws.com/bucket/exam-config.seb?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
```

### Configuration File (exam-config.seb)
```json
{
  "startURL": "https://topin-assessment-portal-beta.earlywave.in/exam"
}
```

### Final Browser URL
```
https://topin-assessment-portal-beta.earlywave.in/exam?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
```

## Implementation Details

### Files Modified
1. **SessionConfiguration.cs** - Added `LoginToken` property
2. **BrowserSettings.cs** - Added `LoginToken` property  
3. **ConfigurationOperation.cs** - Added token extraction and copying logic
4. **BrowserApplication.cs** - Added token injection to start URL
5. **DataValues.cs** - Added default configuration

### Security Features
- Token is cleared from memory after first use
- Proper URL encoding/decoding
- Error handling for malformed URLs
- Logging for debugging and audit trail

## Testing

### Manual Test
1. Create a .seb file with your desired start URL
2. Upload to S3 or host somewhere accessible
3. Create magic link: `tsb://your-server.com/config.seb?token=test123`
4. Click the link - TSB should open and navigate to: `your-start-url?token=test123`

### Expected Behavior
- ✅ Token extracted from URL handler
- ✅ Configuration file downloaded and loaded
- ✅ Token appended to configured start URL
- ✅ Browser navigates to final URL with token
- ✅ Proper logging throughout the process

## Benefits
- Seamless magic link experience for users
- No changes needed to existing .seb files
- Backward compatible with existing functionality
- Configurable and secure token handling
- Works with any start URL configuration


