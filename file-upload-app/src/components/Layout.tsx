import React, { type ReactNode }  from 'react';
import Container from '@mui/material/Container';
import NavBar from './Navbar';

interface LayoutProps {
  children: ReactNode;
}


const Layout: React.FC<LayoutProps> = ({ children }) => (
  <>
    <NavBar />
    <Container maxWidth="md">
      {children}
    </Container>
  </>
);

export default Layout;