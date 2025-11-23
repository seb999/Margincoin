/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      colors: {
        // Trading colors
        profit: '#10b981',
        loss: '#ef4444',
        buy: '#22c55e',
        sell: '#ef4444',
      },
    },
  },
  plugins: [],
}
