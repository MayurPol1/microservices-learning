// Settings for production builds (`ng build`). The browser calls these URLs directly,
// so they are addresses as seen from the user's machine, not Docker service names.
export const environment = {
  /** The API gateway: the only backend address the web app knows. */
  apiBaseUrl: 'http://localhost:5000',
  /** Aspire Dashboard, used to link an order to its trace. */
  dashboardUrl: 'http://localhost:18888',
};
