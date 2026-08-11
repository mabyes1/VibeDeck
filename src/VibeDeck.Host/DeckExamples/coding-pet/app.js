const cat = document.getElementById("cat");
const status = document.getElementById("status");
const focusMinutes = document.getElementById("focusMinutes");
const keystrokes = document.getElementById("keystrokes");
const boop = document.getElementById("boop");

const startedAt = Date.now();
let imaginaryKeys = 0;
const phrases = [
  "compiling tiny victories…",
  "guarding the event loop…",
  "reviewing one suspicious semicolon…",
  "pretending the bug is a feature…"
];

setInterval(() => {
  focusMinutes.textContent = String(Math.floor((Date.now() - startedAt) / 60000)).padStart(2, "0");
  imaginaryKeys += 7 + Math.floor(Math.random() * 19);
  keystrokes.textContent = String(imaginaryKeys).padStart(3, "0");
}, 1000);

setInterval(() => {
  status.textContent = phrases[Math.floor(Math.random() * phrases.length)];
}, 4200);

boop.addEventListener("click", () => {
  cat.classList.remove("boop");
  requestAnimationFrame(() => cat.classList.add("boop"));
  status.textContent = "purr request accepted ✓";
});
