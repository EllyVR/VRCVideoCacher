import { getYoutubeCookiesDebounce, getYoutubeCookies } from "./utils.mjs";

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg === "send_youtube_cookies") {
    setTimeout(() => {
      getYoutubeCookies();
    }, 2000);
  }
});
chrome.cookies.onChanged.addListener((changeInfo) =>
  getYoutubeCookiesDebounce(changeInfo),
);
chrome.runtime.onInstalled.addListener(async () => {
  console.log("Extension installed, fetching YouTube cookies...");
  await getYoutubeCookies();
});
