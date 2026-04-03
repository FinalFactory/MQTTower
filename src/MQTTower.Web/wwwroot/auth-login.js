// Browser login for Blazor Server: cookie auth must not run inside the SignalR circuit
// (response headers are already committed). POST here so Set-Cookie applies to the document.
window.mqttowerAuth = {
  login: async function (url, userName, password) {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ userName: userName, password: password }),
      credentials: 'same-origin',
    });
    if (res.status === 401) {
      return false;
    }
    if (!res.ok) {
      throw new Error('Login request failed: ' + res.status);
    }
    return true;
  },
};
