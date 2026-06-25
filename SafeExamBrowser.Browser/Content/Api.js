/*
 * Copyright (c) 2025 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

TopinSecureBrowser = {
    version: 'TSB_Windows_%%_VERSION_%%',
    security: {
        browserExamKey: '%%_BEK_%%',
        configKey: '%%_CK_%%',
        updateKeys: (callback) => callback()
    },
    log: {
        // website -> native: provide session context + short-lived Cognito credentials
        setUploadConfig: function (cfg) {
            CefSharp.PostMessage({ Type: 'LogUploadConfig', Payload: cfg });
        },
        // website assigns this; native calls _requestCredentials() when credentials expire
        onCredentialsRequired: null,
        _requestCredentials: function () {
            if (typeof this.onCredentialsRequired === 'function') {
                this.onCredentialsRequired();
            }
        },
        // TEMPORARY DEBUG: native calls _debug() to surface S3 log-upload steps/errors on-page (dev tools are blocked)
        _debug: function (message) {
            try {
                var panel = document.getElementById('__tsb_log_debug__');

                if (!panel) {
                    panel = document.createElement('div');
                    panel.id = '__tsb_log_debug__';
                    panel.style.cssText = [
                        'position:fixed',
                        'top:10px',
                        'right:10px',
                        'width:380px',
                        'max-height:40vh',
                        'overflow-y:auto',
                        'z-index:2147483647',
                        'background:rgba(0,0,0,0.85)',
                        'color:#0f0',
                        'font-family:monospace',
                        'font-size:11px',
                        'line-height:1.4',
                        'padding:8px',
                        'border:1px solid #0f0',
                        'border-radius:4px',
                        'white-space:pre-wrap',
                        'word-break:break-word'
                    ].join(';');

                    var header = document.createElement('div');
                    header.style.cssText = 'display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;color:#fff;font-weight:bold;';

                    var title = document.createElement('span');
                    title.textContent = 'S3 Log Upload Debug';
                    header.appendChild(title);

                    var hide = document.createElement('button');
                    hide.textContent = 'hide';
                    hide.style.cssText = 'background:#333;color:#fff;border:1px solid #777;border-radius:3px;cursor:pointer;font-size:11px;';
                    hide.onclick = function () { panel.style.display = 'none'; };
                    header.appendChild(hide);

                    panel.appendChild(header);

                    var body = document.createElement('div');
                    body.id = '__tsb_log_debug_body__';
                    panel.appendChild(body);

                    document.body.appendChild(panel);
                }

                panel.style.display = 'block';

                var body = document.getElementById('__tsb_log_debug_body__');
                var line = document.createElement('div');
                var time = new Date().toISOString().substr(11, 12);
                line.textContent = '[' + time + '] ' + message;
                body.appendChild(line);

                panel.scrollTop = panel.scrollHeight;
            } catch (e) {
                // ignore: debugging must never break the page
            }
        }
    }
}

// Backward-compatibility alias (clipboard.js and existing integrations use SafeExamBrowser)
SafeExamBrowser = TopinSecureBrowser;