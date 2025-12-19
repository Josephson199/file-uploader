import React from 'react';
import { Link } from 'react-router-dom';
import Button from '@mui/material/Button';
import AppBar from '@mui/material/AppBar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import useKeycloak from '../hooks/useKeycloak';

interface NavBarProps {}

const NavBar: React.FC<NavBarProps> = () => {
  const { keycloak, authenticated, login, logout } = useKeycloak();

  const handleLogin = () => {
    if(login) login();
  };

  const handleLogout = () => {
    if(logout) logout();
  };

  return (
    <AppBar position="fixed">
      <Toolbar>
        <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
          <Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>
            Sandbox Application
          </Link>
        </Typography>
        {authenticated ? (
          <>
            <Button color="inherit" component={Link} to="/upload">
              My Uploads
            </Button>
            <Button color="inherit" onClick={handleLogout}>
              Logout
            </Button>
          </>
        ) : (
          <Button color="inherit" onClick={handleLogin}>
            Login
          </Button>
        )}
      </Toolbar>
    </AppBar>
  );
};

export default NavBar;