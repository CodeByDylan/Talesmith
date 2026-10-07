/** The documentation sections. Each one has its own content folder, sidebar file, route and screenshot folder. */
export const sections = [
  {id: 'guide', title: 'Guide'},
  {id: 'scripting', title: 'Scripting'},
  {id: 'plugins', title: 'Plugins'},
  {id: 'developers', title: 'Developers'},
] as const;

export type SectionId = (typeof sections)[number]['id'];
